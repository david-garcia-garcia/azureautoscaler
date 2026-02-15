using System.Runtime.ExceptionServices;
using System.Text.Json;
using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.ResourceGraph;
using Azure.ResourceManager.ResourceGraph.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using poolautoscaler.configuration;
using poolautoscaler.resourcemanagement.Dto;
using poolautoscaler.metrics.Dto;
using poolautoscaler.utils;

namespace poolautoscaler.resourcemanagement
{
    /// <summary>
    /// Base class for resource state tracking.
    /// </summary>
    public abstract class ResourceState
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
        /// Gets or sets the change history snapshots.
        /// </summary>
        public List<ResourceHistoryItem> ChangeHistory { get; set; }

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
        protected ResourceState(string id, ILogger logger, Resource configuration)
        {
            this.ResourceId = id;
            this.Logger = logger;
            this.Configuration = configuration;
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
        /// </summary>
        /// <returns>The resource ID.</returns>
        protected virtual string GetResourceIdForChangeHistory()
        {
            return this.ResourceId;
        }

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

            // This gives visiblity - wihtout flooding the logs - that the resource was disabled externally
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

            bool initialRefresh = this.ChangeHistory == null;

            // Initialize resource history
            if (this.ChangeHistory == null)
            {
                this.ChangeHistory = new List<ResourceHistoryItem>();
            }

            // Grab the activity logs. Ojo porque no es el registro de cambios...
            // LogsQueryClient c = new LogsQueryClient(credential, new LogsQueryClientOptions() { });
            // var r = await c.QueryResourceAsync(this.Resource.Id, "AzureActivity", QueryTimeRange.All, new LogsQueryOptions(), cancellationToken);

            // Grab the changelogs
            var tenantResource = client.GetTenants().First();

            // Only keep snapshots from the last 72 hours to reduce memory usage
            const int snapshotRetentionHours = 72;
            var retentionCutoff = DateTime.UtcNow.AddHours(-snapshotRetentionHours);

            // Purge snapshots older than 72 hours
            if (this.ChangeHistory.Any())
            {
                var originalCount = this.ChangeHistory.Count;
                this.ChangeHistory = this.ChangeHistory.Where(s => s.Timestamp > retentionCutoff).ToList();
                var purgedCount = originalCount - this.ChangeHistory.Count;
                if (purgedCount > 0)
                {
                    this.Logger.LogTrace("Purged {0} snapshots older than {1} hours", purgedCount, snapshotRetentionHours);
                }
            }

            var mostRecentTimestamp = this.ChangeHistory.FirstOrDefault()?.Timestamp;
            var timeFilter = mostRecentTimestamp.HasValue ? $"and timestamp > datetime('{mostRecentTimestamp.Value:O}')" : string.Empty;
            var resourceIdFilter = this.GetResourceIdForChangeHistory();

            // Null here means no resource history should be loaded.
            if (string.IsNullOrWhiteSpace(resourceIdFilter))
            {
                if (initialRefresh)
                {
                    this.Logger.LogInformation("Resource change history is not available and will not be read.");
                }

                return;
            }

            int page = 0;
            int loadedSnapshots = 0;

            while (true)
            {
                var itemsPerPage = 100;

                var changeLog = await tenantResource.GetResourceHistoryAsync(
                    new ResourcesHistoryContent()
                    {
                        Query = $"where id =~ '{resourceIdFilter}' {timeFilter} | order by timestamp desc",
                        Options = new ResourcesHistoryRequestOptions()
                    {
                        // Only query last 72 hours to reduce memory usage
                        Interval = new DateTimeInterval(retentionCutoff, DateTimeOffset.UtcNow),
                        Skip = page * itemsPerPage,
                        Top = itemsPerPage
                    }
                    },
                    cancellationToken);

                // First deserialize the array of changes from
                var r = JsonSerializer.Deserialize<ResourceHistoryResponse>(changeLog.Value);

                loadedSnapshots += r.Count;

                if (r.Count == 0)
                {
                    break;
                }

                this.Logger.LogTrace("Loaded {0} state snapshots in page {1}", loadedSnapshots, page);

                var existing = this.ChangeHistory;

                this.ChangeHistory = r.Snapshots;
                this.ChangeHistory.AddRange(existing);

                if (r.Count < itemsPerPage)
                {
                    break;
                }

                int maxHistory = 100;

                if (this.ChangeHistory.Count >= maxHistory)
                {
                    this.Logger.LogWarning($"Max history limit {this.ChangeHistory.Count}/{maxHistory} reached. History load will be interrupted.");
                    break;
                }

                page++;
            }

            if (this.ChangeHistory.Count == 0)
            {
                this.Logger.LogInformation("Resource snapshot history could not be retrieved. No data available.");
            }
            else if (loadedSnapshots > 0)
            {
                this.Logger.LogInformation("Loaded {0} new state snapshots with a total of {1}", loadedSnapshots, this.ChangeHistory.Count);
            }

            // This is helpful to detect if resources were scaled manually outside the tool and honour cooldowns and other rules
            // TODO: Not every change means actually a rescale or downtime. This change history can contain minor changes that are not relevant
            // and should not be taken into consideration. This is specific to each type of resource.
            if (this.LastScale == null && this.ChangeHistory.Any())
            {
                var lastChange = this.ChangeHistory.First().Timestamp;
                var timeAgo = DateTime.UtcNow - lastChange;
                this.Logger.LogInformation("Loaded last change for resource from change history at {0} ({1} ago)", lastChange, timeAgo.ToString(@"hh\:mm\:ss\.f"));

                if (this.LastScale == null || lastChange.DateTime > this.LastScale)
                {
                    this.LastScale = lastChange.DateTime;
                }
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
        /// Get the effective SKU that was running at one point in time based on historical
        /// resource snapshots.
        /// </summary>
        /// <param name="targetTime">Target time.</param>
        /// <param name="useSmallestSku">Use smallest SKU.</param>
        /// <returns>Effective SKU name.</returns>
        /// <exception cref="InvalidOperationException">Thrown when no change history is available or no history exists before the target time.</exception>
        public string GetEffectiveSkuAtPointInTime(DateTimeOffset targetTime, bool useSmallestSku = true)
        {
            if (this.ChangeHistory == null || !this.ChangeHistory.Any())
            {
                throw new InvalidOperationException("No change history available");
            }

            // Order changes from oldest to newest up to our target time
            var relevantChanges = this.ChangeHistory
                .Where(s => s.Timestamp <= targetTime)
                .OrderBy(s => s.Timestamp)
                .ToList();

            if (!relevantChanges.Any())
            {
                throw new InvalidOperationException($"No change history available before {targetTime}");
            }

            // Get the smallest SKU by vCores
            return relevantChanges
                .Last()
                .Sku
                .Name;
        }

        /// <summary>
        /// Gets the timestamp of the latest SKU change in history, or null if none.
        /// </summary>
        /// <returns>Timestamp of latest SKU change, or null.</returns>
        public DateTimeOffset? GetLatestSkuChange()
        {
            if (this.ChangeHistory == null || !this.ChangeHistory.Any())
            {
                return null;
            }

            // Order by timestamp descending (newest first)
            var orderedChanges = this.ChangeHistory
                .OrderByDescending(ch => ch.Timestamp)
                .Where(ch => ch.Sku != null)
                .ToList();

            // Look for actual SKU changes by comparing consecutive entries
            for (int i = 0; i < orderedChanges.Count - 1; i++)
            {
                if (orderedChanges[i].Sku.Name != orderedChanges[i + 1].Sku.Name)
                {
                    return orderedChanges[i].Timestamp;
                }
            }

            return null;
        }

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
