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

            patch.Sku = this.RequestedMssqlElasticPoolState.Sku.DeepCopy() ?? this.ExistingMssqlElasticPoolState.Sku.DeepCopy();

            patch.MaxSizeBytes = this.RequestedMssqlElasticPoolState.MaxSizeBytes ?? this.ExistingMssqlElasticPoolState.MaxSizeBytes;

            var minValidCapacityForCurrentUsage = MssqlElasticPoolResourceStateHelper.FindClosesValidStorageSize(currentStorage);
            if (minValidCapacityForCurrentUsage > patch.MaxSizeBytes)
            {
                patch.MaxSizeBytes = minValidCapacityForCurrentUsage;
            }

            patch.MaxSizeBytes = MssqlElasticPoolResourceStateHelper.FindClosesValidStorageSize((long)patch.MaxSizeBytes);

            (var targetDtu, var targetMaxDataBytes) = MssqlElasticPoolResourceStateHelper.FindClosestDtuThatCanHoldStorage(patch.Sku, patch.Sku.Capacity.Value, (long)patch.MaxSizeBytes);
            patch.Sku.Capacity = (int)targetDtu;

            patch.PerDatabaseMaxCapacity = this.RequestedMssqlElasticPoolState.PerDatabaseMaxCapacity;

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

            var result = await this.Resource.UpdateAsync(Azure.WaitUntil.Completed, patch, cancellationToken);
            this.ValidateArmResult(result);
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
