using Azure.Core;
using Azure.Monitor.Query;
using Azure.ResourceManager;
using Azure.ResourceManager.Sql;
using Azure.ResourceManager.Sql.Models;
using Microsoft.Extensions.Logging;
using poolautoscaler.configuration;
using poolautoscaler.metrics;
using poolautoscaler.resourcemanagement;
using poolautoscaler.resourcemanagement.Dto;
using poolautoscaler.resources.MssqlElasticPool.Dto;
using poolautoscaler.utils;

namespace poolautoscaler.resources.MssqlElasticPool
{
    /// <summary>
    /// Resource state for an Azure SQL Elastic Pool.
    /// </summary>
    public class MssqlElasticPoolResourceState : ResourceState
    {
        /// <summary>
        /// Accumulated storage bump applied to recovery patches when the pool is stuck below actual data size.
        /// Incremented by 50 GB each time <c>ElasticPoolDecreaseStorageLimitBelowUsage</c> is received on a
        /// scale-up. Reset automatically in <see cref="PreparePatch"/> once the pool is no longer at capacity.
        /// </summary>
        private long storageRecoveryBumpBytes = 0L;

        /// <inheritdoc />
        public override object ExistingStateRaw => this.ExistingMssqlElasticPoolState;

        /// <inheritdoc />
        public override object RequestedStateRaw => this.RequestedMssqlElasticPoolState;

        /// <summary>
        /// Gets or sets the requested elastic pool state (SKU, max size).
        /// </summary>
        public MssqlElasticPoolState RequestedMssqlElasticPoolState { get; set; }

        /// <summary>
        /// Gets or sets the current existing elastic pool state from Azure.
        /// </summary>
        public MssqlElasticPoolState ExistingMssqlElasticPoolState { get; set; }

        /// <summary>
        /// Gets the elastic pool ARM resource (strongly typed).
        /// </summary>
        public new ElasticPoolResource Resource => (ElasticPoolResource)base.Resource;

        /// <summary>
        /// Initializes a new instance of the <see cref="MssqlElasticPoolResourceState"/> class.
        /// </summary>
        /// <param name="id">The elastic pool resource ID.</param>
        /// <param name="logger">The logger.</param>
        /// <param name="resourceConfiguration">The resource configuration.</param>
        /// <param name="resourceLocationResolver">Optional resource location resolver.</param>
        /// <param name="vmSizeResolver">Optional VM size resolver.</param>
        public MssqlElasticPoolResourceState(
            string id,
            ILogger logger,
            Resource resourceConfiguration,
            IResourceLocationResolver? resourceLocationResolver = null,
            IVmSizeResolver? vmSizeResolver = null)
            : base(id, logger, resourceConfiguration, resourceLocationResolver, vmSizeResolver)
        {
            if (!ResourceStateFactory.ElasticPools.IsMatch(id))
            {
                throw new ArgumentException("Invalid Elastic Pool resource ID", nameof(id));
            }
        }

        /// <summary>
        /// Sets the requested DTU capacity (only increases if already set).
        /// </summary>
        /// <param name="dtuCapacity">The DTU capacity.</param>
        public void SetDtuCapacity(int dtuCapacity)
        {
            if (this.RequestedMssqlElasticPoolState.Sku == null)
            {
                this.RequestedMssqlElasticPoolState.Sku = this.ExistingMssqlElasticPoolState.Sku.DeepCopy();
                this.RequestedMssqlElasticPoolState.Sku.Capacity = dtuCapacity;
                return;
            }

            if (dtuCapacity < this.RequestedMssqlElasticPoolState.Sku.Capacity)
            {
                return;
            }

            this.RequestedMssqlElasticPoolState.Sku.Capacity = dtuCapacity;
        }

        /// <summary>
        /// Sets the requested max size in bytes (only increases if already set).
        /// </summary>
        /// <param name="maxSizeBytes">The maximum size in bytes.</param>
        public void SetMaxSizeBytes(long maxSizeBytes)
        {
            if (this.RequestedMssqlElasticPoolState.MaxSizeBytes == null)
            {
                this.RequestedMssqlElasticPoolState.MaxSizeBytes = maxSizeBytes;
                return;
            }

            if (maxSizeBytes < this.RequestedMssqlElasticPoolState.MaxSizeBytes)
            {
                return;
            }

            this.RequestedMssqlElasticPoolState.MaxSizeBytes = maxSizeBytes;
        }

        /// <summary>
        /// Sets requested per-database max eDTU, snapped to a valid tier and effective pool ceiling.
        /// </summary>
        /// <param name="perDatabaseMaxCapacity">Requested per-database max eDTU.</param>
        public void SetPerDatabaseMaxCapacity(int perDatabaseMaxCapacity)
        {
            var sku = this.Resource.Data.Sku;
            var poolDtu = sku.Capacity ?? throw new InvalidOperationException("Elastic pool SKU capacity is not set.");
            var snapped = MssqlElasticPoolResourceStateHelper.SnapToNearestPerDbMaxCapacity(sku, (int)poolDtu, perDatabaseMaxCapacity);
            this.RequestedMssqlElasticPoolState.PerDatabaseMaxCapacity = snapped;
        }

        /// <inheritdoc />
        public override ResourcePatchOperation PreparePatch()
        {
            ResourcePatchOperation result = new ResourcePatchOperation();

            var patch = new MssqlElasticPoolState();

            long currentStorage = (long)(this.ExistingMssqlElasticPoolState.CurrentUsedStorage ?? 0);
            long existingMaxSizeBytes = (long)(this.ExistingMssqlElasticPoolState.MaxSizeBytes ?? 0);

            patch.Sku = this.RequestedMssqlElasticPoolState.Sku.DeepCopy() ?? this.ExistingMssqlElasticPoolState.Sku.DeepCopy();

            patch.MaxSizeBytes = this.RequestedMssqlElasticPoolState.MaxSizeBytes ?? this.ExistingMssqlElasticPoolState.MaxSizeBytes;

            var minValidCapacityForCurrentUsage = MssqlElasticPoolResourceStateHelper.FindClosesValidStorageSize(currentStorage);
            if (minValidCapacityForCurrentUsage > patch.MaxSizeBytes)
            {
                patch.MaxSizeBytes = minValidCapacityForCurrentUsage;
            }

            // Guard 1: CurrentUsedStorage = 0 signals a broken storage_used metric reading.
            // A pool with any databases will never have zero bytes in use, so zero means the
            // metric failed. Suppress any storage reduction to avoid setting MaxSizeBytes below
            // actual data while the metric is unavailable.
            if (currentStorage == 0 && existingMaxSizeBytes > 0 && patch.MaxSizeBytes < existingMaxSizeBytes)
            {
                this.Logger.LogWarning(
                    "CurrentUsedStorage=0 with existing MaxSizeBytes={ExistingMax}. Suppressing storage reduction from {Proposed} to prevent data loss from a broken metric.",
                    existingMaxSizeBytes,
                    patch.MaxSizeBytes);
                patch.MaxSizeBytes = existingMaxSizeBytes;
            }

            // Recovery bump: added when the pool is stuck (MaxSizeBytes < actual data).
            // Each ElasticPoolDecreaseStorageLimitBelowUsage on a scale-up increments this by 50 GB
            // until the patch target clears the actual data size and Azure accepts it.
            if (this.storageRecoveryBumpBytes > 0)
            {
                patch.MaxSizeBytes = (long)patch.MaxSizeBytes + this.storageRecoveryBumpBytes;
            }

            patch.MaxSizeBytes = MssqlElasticPoolResourceStateHelper.FindClosesValidStorageSize((long)patch.MaxSizeBytes);

            (var targetDtu, var targetMaxDataBytes) = MssqlElasticPoolResourceStateHelper.FindClosestDtuThatCanHoldStorage(patch.Sku, patch.Sku.Capacity.Value, (long)patch.MaxSizeBytes);
            patch.Sku.Capacity = (int)targetDtu;

            patch.PerDatabaseMaxCapacity = this.RequestedMssqlElasticPoolState.PerDatabaseMaxCapacity;

            // Re-clamp per-DB max against the *target* pool DTU.  If both dimensions are
            // scaled down simultaneously the value was snapped against the old pool DTU and
            // may now exceed the new ceiling, causing Azure to reject the combined patch.
            if (patch.PerDatabaseMaxCapacity != null && patch.Sku?.Capacity != null)
            {
                patch.PerDatabaseMaxCapacity = MssqlElasticPoolResourceStateHelper.SnapToNearestPerDbMaxCapacity(
                    patch.Sku, (int)patch.Sku.Capacity.Value, (int)patch.PerDatabaseMaxCapacity.Value);
            }

            bool hasChanges = (patch.Sku != null && patch.Sku.Capacity != this.ExistingMssqlElasticPoolState.Sku.Capacity)
                              || (patch.MaxSizeBytes != null && patch.MaxSizeBytes != this.ExistingMssqlElasticPoolState.MaxSizeBytes)
                              || (patch.PerDatabaseMaxCapacity != null
                                  && patch.PerDatabaseMaxCapacity != this.ExistingMssqlElasticPoolState.PerDatabaseMaxCapacity);

            result.PatchData = patch;
            result.HasChanges = hasChanges;
            result.Disruptive = hasChanges && patch.Sku?.Capacity != this.ExistingMssqlElasticPoolState.Sku.Capacity;
            return result;
        }

        /// <inheritdoc />
        private static readonly Dictionary<string, int> TransientErrorDisableMinutes = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["ElasticPoolUpdateLinksNotInCatchup"] = 10,
            ["ElasticPoolBusy"] = 10,
        };

        /// <inheritdoc/>
        public override async Task ApplyChanges(ResourcePatchOperation operation, CancellationToken cancellationToken)
        {
            if (!(operation.PatchData is MssqlElasticPoolState internalPatch))
            {
                throw new ArgumentException(
                    $"Patch data must be {nameof(MssqlElasticPoolState)}; actual type was '{operation.PatchData?.GetType().FullName ?? "null"}'.");
            }

            ElasticPoolPatch patch = new ElasticPoolPatch();

            patch.Sku = internalPatch.Sku;
            patch.MaxSizeBytes = internalPatch.MaxSizeBytes;

            patch.PerDatabaseSettings = new ElasticPoolPerDatabaseSettings();
            int? maxPerDb = internalPatch.PerDatabaseMaxCapacity ?? patch.Sku.Capacity;
            patch.PerDatabaseSettings.MaxCapacity = maxPerDb.HasValue ? maxPerDb.Value : null;

            try
            {
                var result = await this.Resource.UpdateAsync(Azure.WaitUntil.Completed, patch, cancellationToken);
                this.ValidateArmResult(result);
                if (this.storageRecoveryBumpBytes > 0)
                {
                    this.Logger.LogInformation(
                        "Storage recovery bump reset after successful apply. Normal autoscaling resumes.");
                    this.storageRecoveryBumpBytes = 0;
                }
            }
            catch (Azure.RequestFailedException rfex)
                when (rfex.ErrorCode == "ElasticPoolDecreaseStorageLimitBelowUsage"
                      && internalPatch.MaxSizeBytes > this.ExistingMssqlElasticPoolState.MaxSizeBytes)
            {
                // Pool is stuck: MaxSizeBytes is below actual data. Our scale-up target was still
                // not enough. Accumulate 50 GB and retry in 5 minutes so the next patch overshoots
                // the actual data usage without needing a priori knowledge of it.
                this.storageRecoveryBumpBytes += 50L * 1024L * 1024L * 1024L;
                this.Logger.LogCritical(
                    "Pool stuck: ElasticPoolDecreaseStorageLimitBelowUsage on scale-up (target={Target}, existing={Existing}). "
                    + "Recovery bump is now {Bump} GB. Retrying in 5 minutes.",
                    internalPatch.MaxSizeBytes,
                    this.ExistingMssqlElasticPoolState.MaxSizeBytes,
                    this.storageRecoveryBumpBytes / (1024L * 1024L * 1024L));
                throw new TransientAzureOperationException("ElasticPoolDecreaseStorageLimitBelowUsage", 5, rfex);
            }
            catch (Azure.RequestFailedException rfex)
                when (!string.IsNullOrEmpty(rfex.ErrorCode) && TransientErrorDisableMinutes.TryGetValue(rfex.ErrorCode, out _))
            {
                throw new TransientAzureOperationException(rfex.ErrorCode, TransientErrorDisableMinutes[rfex.ErrorCode], rfex);
            }
        }

        /// <inheritdoc />
        protected override async Task InternalRefreshAsync(ArmClient client, TokenCredential credential, CancellationToken cancellationToken)
        {
            base.Resource = await client.GetElasticPoolResource(new ResourceIdentifier(this.ResourceId)).GetAsync(cancellationToken);

            var metricsClient = new MetricsQueryClient(credential);
            MetricEvaluation eval = new MetricEvaluation(this.Logger);

            var metricResult = eval.RetrieveHistory(metricsClient, this.ResourceId, "storage_used", TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1), cancellationToken, null, null).Result;
            var values = metricResult.Values;
            values.Reverse();

            var storage_used = values.Take(1)?.Select((i) => i.Average).Average();

            this.ResourceTagsPopulate(this.Resource.Data.Tags);

            this.ExistingMssqlElasticPoolState = new MssqlElasticPoolState
            {
                MaxSizeBytes = this.Resource.Data.MaxSizeBytes,
                Sku = this.Resource.Data.Sku,
                CurrentUsedStorage = (long?)storage_used,
                PerDatabaseMaxCapacity = this.Resource.Data.PerDatabaseSettings?.MaxCapacity is double liveMaxCap
                    ? (int)liveMaxCap
                    : null,
            };

            this.RequestedMssqlElasticPoolState = new MssqlElasticPoolState();
        }
    }
}
