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
    public class MssqlElasticPoolResourceState : ResourceState
    {
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

            patch.PerDatabaseSettings = new ElasticPoolPerDatabaseSettings();
            patch.PerDatabaseSettings.MaxCapacity = patch.Sku.Capacity;

            var result = await this.Resource.UpdateAsync(Azure.WaitUntil.Completed, patch, cancellationToken);
            this.ValidateArmResult(result);
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

            this.PopulateResourceTags(this.Resource.Data.Tags);

            this.ExistingMssqlElasticPoolState = new MssqlElasticPoolState()
            {
                MaxSizeBytes = this.Resource.Data.MaxSizeBytes,
                Sku = this.Resource.Data.Sku,
                CurrentUsedStorage = (long?)storage_used
            };

            this.RequestedMssqlElasticPoolState = new MssqlElasticPoolState();
        }
    }
}
