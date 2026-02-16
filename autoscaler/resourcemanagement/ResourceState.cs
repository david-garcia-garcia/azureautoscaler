using System.Runtime.ExceptionServices;
using System.Text.Json;
using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.ResourceGraph;
using Azure.ResourceManager.ResourceGraph.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
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
        /// May be null when not configured.
        /// </summary>
        public IVmSizeResolver? VmSizeResolver { get; }

        /// <summary>
        /// Gets or sets the memory cache for this resource state.
        /// </summary>
        protected IMemoryCache Cache { get; set; }

        /// <summary>
        /// Gets the resource identifier.
        /// </summary>
        protected readonly string ResourceId;

        /// <summary>
        /// Populates ResourceTags from the provided tags dictionary.
        /// </summary>
        /// <param name="tags">Dictionary of tags to populate from.</param>
        protected void PopulateResourceTags(IDictionary<string, string> tags)
        {
            this.ResourceTags.Clear();

            foreach (var tag in tags)
            {
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
        /// <param name="vmSizeResolver">Optional VM size resolver.</param>
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

        private const string AutoscalerDisabledTag = "autoscaler.disabled";

        private DateTime? LastEvaluation;

        /// <summary>
        /// Refreshes the resource state from Azure.
        /// </summary>
        /// <param name="client">The ARM client.</param>
        /// <param name="credential">The token credential.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task that completes when refresh is done.</returns>
        public virtual async Task Refresh(ArmClient client, TokenCredential credential, CancellationToken cancellationToken)
        {
            this.Logger.LogTrace("Starting resource refresh");

            var isCurrentlyDisabled = this.DisabledUntil.ContainsKey(AutoscalerDisabledTag);

            try
            {
                await this.InternalRefreshAsync(client, credential, cancellationToken);
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

            var location = this.Resource?.Id?.Location;
            CustomMetricHelpers helpers = new CustomMetricHelpers();
            if (this.VmSizeResolver != null &&
                this.ResourceParts.TryGetValue("subscriptionId", out var subscriptionId) &&
                location.HasValue)
            {
                helpers = new CustomMetricHelpers(subscriptionId, location.Value, this.VmSizeResolver, client, cancellationToken);
            }

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
