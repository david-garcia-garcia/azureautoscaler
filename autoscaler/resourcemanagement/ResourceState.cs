using System.Runtime.ExceptionServices;
using System.Text.Json;
using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.ResourceGraph;
using Azure.ResourceManager.ResourceGraph.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using poolautoscaler.configuration;
using poolautoscaler.metrics;
using poolautoscaler.metrics.Dto;
using poolautoscaler.resourcemanagement.Dto;
using poolautoscaler.utils;

namespace poolautoscaler.resourcemanagement
{
    /// <summary>
    /// Base class for resource state tracking.
    /// </summary>
    public abstract class ResourceState : ICustomMetricDataProvider
    {
        /// <summary>
        /// Gets or sets a dictionary of reasons and until when the resource is disabled.
        /// </summary>
        public Dictionary<string, DateTime> DisabledUntil { get; set; } = new Dictionary<string, DateTime>();

        /// <summary>
        /// Gets or sets when the "disabled" message was last logged to avoid log spam.
        /// </summary>
        public DateTime LastDisabledMessageLogged { get; set; } = DateTime.MinValue;

        /// <summary>
        /// Gets the raw existing state of the resource.
        /// </summary>
        public abstract object ExistingStateRaw { get; }

        /// <summary>
        /// Gets the raw requested state of the resource.
        /// </summary>
        public abstract object RequestedStateRaw { get; }

        /// <summary>
        /// Determines whether the resource is currently disabled.
        /// </summary>
        /// <returns>True if the resource is disabled; otherwise false.</returns>
        public bool IsDisabled()
        {
            // Cleanup unused resources
            var expiredKeys = this.DisabledUntil.Where((i) => i.Value != DateTime.MaxValue && (i.Value - DateTime.UtcNow).TotalSeconds < 0).Select((i) => i.Key).ToList();

            foreach (var expiredKey in expiredKeys)
            {
                this.DisabledUntil.Remove(expiredKey);
            }

            if (this.DisabledUntil.Values.Any((i) => i == DateTime.MaxValue))
            {
                return true;
            }

            if (this.DisabledUntil.Values.Any((i) => (i - DateTime.UtcNow).TotalSeconds > 0))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Gets the number of seconds until the next evaluation.
        /// </summary>
        /// <returns>Seconds until next evaluation, or 0 if not applicable.</returns>
        public int NextEvaluationSeconds()
        {
            if (this.LastEvaluation == null)
            {
                return 0;
            }

            return (int)this.Configuration.FrequencyParsed.TotalSeconds - (int)(DateTime.UtcNow - this.LastEvaluation.Value).TotalSeconds;
        }

        /// <summary>
        /// Resets the last evaluation timestamp to now.
        /// </summary>
        public void ResetEvaluation()
        {
            this.LastEvaluation = DateTime.UtcNow;
        }

        /// <summary>
        /// Gets or sets the resource URI path parts (e.g. subscriptionId, resourceGroupName).
        /// </summary>
        public Dictionary<string, string> ResourceParts = new Dictionary<string, string>();

        /// <summary>
        /// Gets or sets the resource tags.
        /// </summary>
        public Dictionary<string, string> ResourceTags = new Dictionary<string, string>();

        /// <summary>
        /// Gets the logger instance.
        /// </summary>
        public readonly ILogger Logger;

        /// <summary>
        /// Gets or sets the ARM resource instance.
        /// </summary>
        public ArmResource? Resource;

        /// <summary>
        /// Gets the resource configuration.
        /// </summary>
        public readonly Resource Configuration;

        /// <summary>
        /// Gets the resource location resolver, used for custom metrics regional endpoints.
        /// May be null when not configured.
        /// </summary>
        public IResourceLocationResolver? ResourceLocationResolver { get; }

        /// <summary>
        /// Gets the VM size resolver, used when building custom metric context for VmSizeToMemory/VmSizeToCores.
        /// Injected into all resource states created by <see cref="ResourceStateFactory"/>.
        /// </summary>
        public IVmSizeResolver? VmSizeResolver { get; }

        /// <summary>
        /// Gets the subscription ID for this resource (when backed by an ARM resource).
        /// Populated during <see cref="Refresh(IArmClientWrapper, TokenCredential, CancellationToken)"/>.
        /// </summary>
        public string? SubscriptionId { get; protected set; }

        /// <summary>
        /// Gets the Azure location for this resource (when backed by an ARM resource).
        /// Populated during <see cref="Refresh(IArmClientWrapper, TokenCredential, CancellationToken)"/>.
        /// </summary>
        public AzureLocation? Location { get; protected set; }

        /// <summary>
        /// Gets or sets the memory cache for this resource state.
        /// </summary>
        protected IMemoryCache Cache { get; set; }

        /// <summary>
        /// Gets the resource identifier.
        /// </summary>
        protected readonly string ResourceId;

        /// <summary>
        /// Populates <see cref="ResourceTags"/> from the provided tags dictionary (clears existing tags first).
        /// </summary>
        /// <param name="tags">Dictionary of tags to populate from.</param>
        protected void ResourceTagsPopulate(IDictionary<string, string>? tags)
        {
            this.ResourceTags.Clear();

            if (tags == null)
            {
                return;
            }

            foreach (var tag in tags)
            {
                this.ResourceTags[tag.Key] = tag.Value;
            }
        }

        /// <summary>
        /// Merges tags into <see cref="ResourceTags"/> without overriding existing keys.
        /// Logs a warning if a key already exists and the incoming value would be ignored.
        /// </summary>
        /// <param name="tags">Dictionary of tags to merge from.</param>
        /// <param name="mergeSourceName">Human-friendly name of the resource/source being merged.</param>
        protected void ResourceTagsMerge(IDictionary<string, string>? tags, string? mergeSourceName)
        {
            if (tags == null)
            {
                return;
            }

            var safeMergeSourceName = string.IsNullOrWhiteSpace(mergeSourceName) ? "<unknown>" : mergeSourceName;

            foreach (var tag in tags)
            {
                if (this.ResourceTags.TryGetValue(tag.Key, out var existingValue))
                {
                    this.Logger.LogWarning(
                        "Resource tag '{TagKey}' already exists; tags from {MergeSourceName} will be ignored for this key. Existing='{ExistingValue}', Incoming='{IncomingValue}'",
                        tag.Key,
                        safeMergeSourceName,
                        existingValue,
                        tag.Value);
                    continue;
                }

                this.ResourceTags[tag.Key] = tag.Value;
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ResourceState"/> class.
        /// </summary>
        /// <param name="id">Resource ID.</param>
        /// <param name="logger">Logger.</param>
        /// <param name="configuration">Resource configuration.</param>
        /// <param name="resourceLocationResolver">Optional resource location resolver.</param>
        /// <param name="vmSizeResolver">VM size resolver.</param>
        protected ResourceState(
            string id,
            ILogger logger,
            Resource configuration,
            IResourceLocationResolver? resourceLocationResolver = null,
            IVmSizeResolver? vmSizeResolver = null)
        {
            this.ResourceId = id;
            this.Logger = logger;
            this.Configuration = configuration;
            this.ResourceLocationResolver = resourceLocationResolver;
            this.VmSizeResolver = vmSizeResolver;
            this.Cache = new MemoryCache(new MemoryCacheOptions() { });
        }

        /// <summary>
        /// Last time this resources was scaled.
        /// </summary>
        /// <summary>
        /// Gets or sets the last time this resource was scaled.
        /// </summary>
        public DateTime? LastScale { get; set; }

        /// <summary>
        /// Gets the resource ID used for change history queries.
        /// Override in derived classes when the history should be read from a related resource.
        /// </summary>
        /// <returns>The resource ID, or null to disable change history.</returns>
        protected virtual string? GetResourceIdForChangeHistory()
        {
            return this.ResourceId;
        }

        private const string AutoscalerDisabledTag = "autoscaler.disabled";

        private DateTime? LastEvaluation;

        /// <summary>
        /// Refreshes the resource state from Azure.
        /// </summary>
        /// <param name="clientWrapper">The ARM client wrapper (provides client and cached tenant).</param>
        /// <param name="credential">The token credential.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task that completes when refresh is done.</returns>
        public virtual async Task Refresh(IArmClientWrapper clientWrapper, TokenCredential credential, CancellationToken cancellationToken)
        {
            this.Logger.LogTrace("Starting resource refresh");

            var isCurrentlyDisabled = this.DisabledUntil.ContainsKey(AutoscalerDisabledTag);

            try
            {
                await this.InternalRefreshAsync(clientWrapper.Client, credential, cancellationToken);
            }
            catch (Azure.RequestFailedException ex)
            {
                // Check if the resource doesn't exist anymore (404) or is unauthorized (403 with specific messages)
                if (ex.Status == 404 || (ex.Status == 403 && IsResourceNotFoundException(ex)))
                {
                    this.Logger.LogWarning("Resource no longer exists in Azure or was not found: {0}", this.ResourceId);
                    throw new ResourceNotFoundException(this.ResourceId, $"Resource {this.ResourceId} no longer exists in Azure or was not found");
                }

                ExceptionDispatchInfo.Capture(ex).Throw();
            }

            if (this.ResourceTags.TryGetValue(AutoscalerDisabledTag, out var autoscalerDisabled) &&
                autoscalerDisabled.ToLower() == "true")
            {
                this.DisabledUntil[AutoscalerDisabledTag] = DateTime.MaxValue;
            }
            else
            {
                this.DisabledUntil.TryRemove(AutoscalerDisabledTag);
            }

            // Populate common ARM properties from the resource as a default.
            // Individual resource states can override SubscriptionId/Location in their InternalRefreshAsync.
            if (this.Resource is ArmResource armResource)
            {
                var id = armResource.Id;

                if (string.IsNullOrEmpty(this.SubscriptionId) && !string.IsNullOrEmpty(id.SubscriptionId))
                {
                    this.SubscriptionId = id.SubscriptionId;
                }

                if (this.Location == null && id.Location.HasValue)
                {
                    this.Location = id.Location.Value;
                }
            }

            // This gives visiblity - without flooding the logs - that the resource was disabled externally
            if (isCurrentlyDisabled != this.DisabledUntil.ContainsKey(AutoscalerDisabledTag))
            {
                if (isCurrentlyDisabled)
                {
                    this.Logger.LogInformation($"Tag '{AutoscalerDisabledTag}' was externally removed from resource.");
                }
                else
                {
                    this.Logger.LogInformation($"Tag '{AutoscalerDisabledTag}' was externally added to resource.");
                }
            }

            // Populate LastScale from ARM change history on first refresh.
            // This is helpful to detect if resources were scaled manually outside the tool (or in previous runs)
            // so that cooldown windows still apply after restarts.
            await this.TryPopulateLastScaleFromChangeHistoryAsync(clientWrapper, cancellationToken);

            if (this.IsDisabled())
            {
                return;
            }

            this.Logger.LogTrace("Refresh finished");
        }

        /// <summary>Custom metric evaluation (override in derived classes).</summary>
        /// <param name="client">The ARM client.</param>
        /// <param name="credential">The token credential.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <param name="setting">The scaling configuration.</param>
        /// <param name="name">The metric name.</param>
        /// <returns>The metric evaluation result.</returns>
        /// <exception cref="NotImplementedException">Thrown when not overridden.</exception>
        public virtual async Task<MetricEvalDtoResult> CustomMetric(
            ArmClient client,
            TokenCredential credential,
            CancellationToken cancellationToken,
            ScalingConfiguration setting,
            string name)
        {
            throw new NotImplementedException("CustomMetric");
        }

        /// <inheritdoc />
        public virtual async Task<CustomMetricDataContext> BuildCustomMetricDataContextAsync(
            ArmClient client,
            TokenCredential credential,
            CancellationToken cancellationToken)
        {
            var extra = await this.GetCustomMetricExtraAsync(client, credential, cancellationToken);
            if (extra == null)
            {
                return null;
            }

            if (string.IsNullOrEmpty(this.SubscriptionId) || this.Location == null)
            {
                throw new InvalidOperationException("Custom metrics that use VM size helpers require SubscriptionId and Location on the resource state.");
            }

            CustomMetricHelpers helpers = new CustomMetricHelpers(this.SubscriptionId, this.Location.Value, this.VmSizeResolver!, client, cancellationToken);

            var context = new CustomMetricDataContext
            {
                Resource = this.Resource,
                ExistingState = this.ExistingStateRaw,
                ResourceParts = new Dictionary<string, string>(this.ResourceParts),
                Helpers = helpers,
                Extra = new Dictionary<string, object>(extra)
            };

            return context;
        }

        /// <summary>
        /// Optional custom forecast for a metric. When a metric has ForecastEnable, this is called first.
        /// Return null to use the generic forecast (history-based); return a result to supply a custom forecast (e.g. resource-specific logic).
        /// </summary>
        /// <param name="metric">The metric configuration (with ForecastEnable and forecast parameters).</param>
        /// <param name="setting">The scaling configuration.</param>
        /// <param name="client">The ARM client.</param>
        /// <param name="credential">The token credential.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The forecast result, or null to fall back to generic forecast.</returns>
        public virtual Task<MetricEvalDtoResult?> GetMetricForecast(
            Metric metric,
            ScalingConfiguration setting,
            ArmClient client,
            TokenCredential credential,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<MetricEvalDtoResult?>(null);
        }

        /// <summary>
        /// Applies all pending changes to the resource.
        /// </summary>
        /// <param name="operation">The patch operation to apply.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task that completes when the apply is done.</returns>
        public abstract Task ApplyChanges(ResourcePatchOperation operation, CancellationToken cancellationToken);

        /// <summary>
        /// Prepares the patch operation for pending changes.
        /// </summary>
        /// <returns>Patch operation to apply.</returns>
        public abstract ResourcePatchOperation PreparePatch();

        /// <summary>
        /// Replaces placeholders in the value with resource parts (e.g. ${subscriptionId}).
        /// </summary>
        /// <param name="value">The string containing placeholders.</param>
        /// <returns>The string with placeholders replaced.</returns>
        public string ReplaceResourceParts(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            foreach (var replacement in this.ResourceParts)
            {
                value = value.Replace("${" + replacement.Key + "}", replacement.Value);
            }

            return value;
        }

        /// <summary>
        /// Override to provide resource-specific extra data (and optional location for VM size cache).
        /// Base returns empty extra and null location (cache uses resource ID location when available).
        /// </summary>
        /// <param name="client">The ARM client.</param>
        /// <param name="credential">The token credential.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Extra dictionary; return null to abort context build.</returns>
        protected virtual Task<IReadOnlyDictionary<string, object>?> GetCustomMetricExtraAsync(
            ArmClient client,
            TokenCredential credential,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyDictionary<string, object>?>(new Dictionary<string, object>());
        }

        /// <summary>
        /// Performs the actual refresh of resource state from Azure.
        /// </summary>
        /// <param name="client">The ARM client.</param>
        /// <param name="credential">The token credential.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task that completes when refresh is done.</returns>
        protected abstract Task InternalRefreshAsync(ArmClient client, TokenCredential credential, CancellationToken cancellationToken);

        /// <summary>
        /// Validates that the ARM operation completed successfully.
        /// </summary>
        /// <typeparam name="T">The result type of the operation.</typeparam>
        /// <param name="operation">The ARM operation to validate.</param>
        protected void ValidateArmResult<T>(ArmOperation<T> operation)
            where T : notnull
        {
            if (operation.HasCompleted == false)
            {
                throw new Exception("Scaling operation did not complete successfully.");
            }
        }

        /// <summary>
        /// Attempts to populate <see cref="LastScale"/> from ARM resource change history.
        /// Uses the wrapper's cached tenant resource to avoid repeated GetTenants calls.
        /// </summary>
        /// <param name="clientWrapper">The ARM client wrapper with cached tenant.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        private async Task TryPopulateLastScaleFromChangeHistoryAsync(
            IArmClientWrapper clientWrapper,
            CancellationToken cancellationToken)
        {
            var resourceIdFilter = this.GetResourceIdForChangeHistory();
            if (string.IsNullOrWhiteSpace(resourceIdFilter))
            {
                this.LastScale = this.LastScale ?? DateTime.MinValue;
                return;
            }

            var tenantResource = clientWrapper.GetTenantResource();
            if (tenantResource == null)
            {
                this.LastScale = this.LastScale ?? DateTime.UtcNow;
                return;
            }

            var intervalStart = new DateTimeOffset(DateTime.UtcNow.AddHours(-72), TimeSpan.Zero);

            if (this.LastScale != null && this.LastScale > DateTime.UtcNow.AddHours(-72))
            {
                intervalStart = new DateTimeOffset(this.LastScale.Value, TimeSpan.Zero);
            }

            var intervalEnd = DateTimeOffset.UtcNow;

            // We recently scaled internally, do not update.
            if ((intervalEnd - intervalStart).TotalSeconds < 60)
            {
                return;
            }

            try
            {
                var history = await tenantResource.GetResourceHistoryAsync(
                    new ResourcesHistoryContent()
                    {
                        Query = $"where id =~ '{resourceIdFilter}' | order by timestamp desc",
                        Options = new ResourcesHistoryRequestOptions()
                        {
                            Interval = new DateTimeInterval(intervalStart, intervalEnd),
                            Skip = 0,
                            Top = 1, // Only need the most recent snapshot
                        },
                    },
                    cancellationToken).ConfigureAwait(false);

                var response = JsonSerializer.Deserialize<ResourceHistoryResponse>(history.Value);
                if (response == null || response.Count == 0 || response.Snapshots == null || response.Snapshots.Count == 0)
                {
                    if (this.LastScale == null)
                    {
                        this.Logger.LogInformation("Could not deterimine last scale from change history. Initializing to DateTime.MinValue");
                        this.LastScale = DateTime.MinValue;
                    }

                    return;
                }

                var lastChange = response.Snapshots[0].Timestamp;
                var lastChangeLocal = lastChange.DateTime;
                var timeAgo = DateTime.UtcNow - lastChangeLocal;

                if (this.LastScale == null)
                {
                    this.Logger.LogInformation(
                        "Initialized last change for resource from change history at {0} ({1} ago)",
                        lastChangeLocal,
                        timeAgo.ToString(@"hh\:mm\:ss"));

                    this.LastScale = lastChangeLocal;
                }
                else if (lastChangeLocal > this.LastScale)
                {
                    this.Logger.LogInformation("Resource was externally manipulated. Last scale updated to {0} ({1} ago)", lastChangeLocal, timeAgo.ToString(@"hh\:mm\:ss"));
                    this.LastScale = lastChangeLocal;
                }
            }
            catch (Exception ex)
            {
                // Change history is best-effort only; failures shouldn't break refresh.
                this.Logger.LogError(ex, "Failed to read resource change history for {ResourceId}", resourceIdFilter);
                this.LastScale = this.LastScale ?? DateTime.MinValue;
            }
        }

        /// <summary>
        /// Determines if a RequestFailedException indicates the resource doesn't exist.
        /// Azure sometimes returns 403 (Forbidden) when a resource is deleted instead of 404 (Not Found).
        /// </summary>
        private static bool IsResourceNotFoundException(Azure.RequestFailedException ex)
        {
            // Common indicators that a resource doesn't exist:
            // 1. "scope is invalid"
            // 2. "does not have authorization" with specific wording about scope
            // 3. Message contains "invalid scope" or "scope" + "invalid"
            var message = ex.Message ?? string.Empty;

            return message.ToLowerInvariant().Contains("scope is invalid") ||
                   (message.ToLowerInvariant().Contains("scope") && message.ToLowerInvariant().Contains("invalid"));
        }
    }
}
