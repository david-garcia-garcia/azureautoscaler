using Azure.Core;
using Azure.Monitor.Query;
using Azure.ResourceManager;
using Azure.ResourceManager.Sql;
using Azure.ResourceManager.Sql.Models;
using Microsoft.Extensions.Logging;
using poolautoscaler.strategies;
using poolautoscaler.utils;

namespace poolautoscaler.resources
{
    public class MssqlElasticPoolResourceState : ResourceState
    {
        public class MssqlElasticPoolState
        {
            // Dtu request
            public SqlSku? Sku { get; set; }

            // Max size request
            public long? MaxSizeBytes { get; set; }

            public long? CurrentUsedStorage { get; set; }
        }

        public override object ExistingStateRaw => this.ExistingMssqlElasticPoolState;

        public override object RequestedStateRaw => this.RequestedMssqlElasticPoolState;

        public MssqlElasticPoolState RequestedMssqlElasticPoolState { get; set; }
        public MssqlElasticPoolState ExistingMssqlElasticPoolState { get; set; }

        public new ElasticPoolResource Resource => (ElasticPoolResource)base.Resource;

        public MssqlElasticPoolResourceState(string id, ILogger logger, Resource resourceConfiguration) : base(id, logger, resourceConfiguration)
        {
            if (!ResourceStateFactory.ElasticPools.IsMatch(id))
            {
                throw new ArgumentException("Invalid Elastic Pool resource ID", nameof(id));
            }
        }

        protected override async Task InternalRefreshAsync(ArmClient client, TokenCredential credential, CancellationToken cancellationToken)
        {
            base.Resource = await client.GetElasticPoolResource(new ResourceIdentifier(this.ResourceId)).GetAsync(cancellationToken);

            var metricsClient = new MetricsQueryClient(credential);
            MetricEvaluation eval = new MetricEvaluation(this.Logger);

            var metricResult = eval.RetrieveHistory(metricsClient, this.ResourceId, "storage_used", TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1), cancellationToken, null, null).Result;
            var values = metricResult.Values;
            values.Reverse();

            var storage_used = values.Take(1)?.Select((i) => i.Average).Average();

            // Populate resource tags
            this.PopulateResourceTags(this.Resource.Data.Tags);

            this.ExistingMssqlElasticPoolState = new MssqlElasticPoolState()
            {
                MaxSizeBytes = this.Resource.Data.MaxSizeBytes,
                Sku = this.Resource.Data.Sku,
                CurrentUsedStorage = (long?)storage_used
            };

            this.RequestedMssqlElasticPoolState = new MssqlElasticPoolState();
        }

        /// <summary>
        /// Set DTU capacity.
        /// </summary>
        /// <param name="dtuCapacity"></param>
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
        /// Set the storage size
        /// </summary>
        /// <param name="maxSizeBytes"></param>
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

        public override ResourcePatchOperation PreparePatch()
        {
            ResourcePatchOperation result = new ResourcePatchOperation();

            var patch = new MssqlElasticPoolState();

            // Get current values
            long currentStorage = (long)(this.ExistingMssqlElasticPoolState.CurrentUsedStorage ?? 0);

            patch.Sku = this.RequestedMssqlElasticPoolState.Sku.DeepCopy() ?? this.ExistingMssqlElasticPoolState.Sku.DeepCopy();

            patch.MaxSizeBytes = this.RequestedMssqlElasticPoolState.MaxSizeBytes ?? this.ExistingMssqlElasticPoolState.MaxSizeBytes;

            // Normalize MaxSizeBytes
            var minValidCapacityForCurrentUsage = MssqlElasticPoolResourceStateHelper.FindClosesValidStorageSize(currentStorage);
            if (minValidCapacityForCurrentUsage > patch.MaxSizeBytes)
            {
                patch.MaxSizeBytes = minValidCapacityForCurrentUsage;
            }
            patch.MaxSizeBytes = MssqlElasticPoolResourceStateHelper.FindClosesValidStorageSize((long)patch.MaxSizeBytes);

            // Ensure Capacity can accomodate storage
            (var targetDtu, var targetMaxDataBytes) = MssqlElasticPoolResourceStateHelper.FindClosestDtuThatCanHoldStorage(patch.Sku, patch.Sku.Capacity.Value, (long)patch.MaxSizeBytes);
            patch.Sku.Capacity = (int)targetDtu;

            bool hasChanges = (patch.Sku != null && patch.Sku.Capacity != this.ExistingMssqlElasticPoolState.Sku.Capacity)
                              || (patch.MaxSizeBytes != null && patch.MaxSizeBytes != this.ExistingMssqlElasticPoolState.MaxSizeBytes);

            result.PatchData = patch;
            result.HasChanges = hasChanges;
            result.Disruptive = hasChanges && patch.Sku?.Capacity != this.ExistingMssqlElasticPoolState.Sku.Capacity;
            return result;
        }

        public override async Task ApplyChanges(ResourcePatchOperation operation, CancellationToken cancellationToken)
        {
            if (!(operation.PatchData is MssqlElasticPoolState internalPatch))
            {
                throw new Exception();
            }

            ElasticPoolPatch patch = new ElasticPoolPatch();

            patch.Sku = internalPatch.Sku;
            patch.MaxSizeBytes = internalPatch.MaxSizeBytes;

            // TODO: This might be an dimention we can actuate on in the future?
            // Keep per-database capacity in sync with full pool capacity.
            patch.PerDatabaseSettings = new ElasticPoolPerDatabaseSettings();
            patch.PerDatabaseSettings.MaxCapacity = patch.Sku.Capacity;

            var result = await Resource.UpdateAsync(Azure.WaitUntil.Completed, patch, cancellationToken);
            this.ValidateArmResult(result);
        }
    }
}