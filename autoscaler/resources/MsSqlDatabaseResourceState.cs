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
    public class MsSqlDatabaseResourceState : ResourceState
    {
        public class MsSqlDatabaseState
        {
            public SqlSku? Sku { get; set; }

            public long? MaxSizeBytes { get; set; }

            public double? CurrentUsedStorage { get; set; }
        }

        public override object ExistingStateRaw => this.ExistingMsSqlDatabaseState;

        public override object RequestedStateRaw => this.RequestedMsSqlDatabaseState;

        public MsSqlDatabaseState RequestedMsSqlDatabaseState { get; set; }
        public MsSqlDatabaseState ExistingMsSqlDatabaseState { get; set; }

        public MsSqlDatabaseResourceState(string id, ILogger logger, Resource resourceConfiguration) : base(id, logger, resourceConfiguration)
        {
            if (!ResourceStateFactory.SqlDatabase.IsMatch(id))
            {
                throw new ArgumentException("Invalid SQL Database resource ID", nameof(id));
            }
        }

        protected override async Task InternalRefreshAsync(ArmClient client, TokenCredential credential, CancellationToken cancellationToken)
        {
            this.Resource = await client.GetSqlDatabaseResource(new ResourceIdentifier(this.ResourceId)).GetAsync(cancellationToken);

            var database = (SqlDatabaseResource)this.Resource;

            var metricsClient = new MetricsQueryClient(credential);
            MetricEvaluation eval = new MetricEvaluation(this.Logger);

            // allocated_data_storage -> what is allocated
            // storage -> what is used
            List<MetricEvalDtoResultValue> values = eval.RetrieveHistory(metricsClient, this.ResourceId, "storage", TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1), cancellationToken, null, null).Result;
            values.Reverse();

            var storage_used = values.Take(1)?.Select((i) => i.Average).Average();

            this.RequestedMsSqlDatabaseState = new MsSqlDatabaseState();

            this.ExistingMsSqlDatabaseState = new MsSqlDatabaseState()
            {
                Sku = database.Data.Sku,
                MaxSizeBytes = database.Data.MaxSizeBytes,
                CurrentUsedStorage = storage_used
            };
        }

        public void SetDtuCapacity(int dtuCapacity)
        {
            if (this.RequestedMsSqlDatabaseState.Sku == null)
            {
                this.RequestedMsSqlDatabaseState.Sku = this.ExistingMsSqlDatabaseState.Sku.DeepCopy();
                this.RequestedMsSqlDatabaseState.Sku.Capacity = dtuCapacity;
                return;
            }

            if (dtuCapacity > this.RequestedMsSqlDatabaseState.Sku.Capacity)
            {
                this.RequestedMsSqlDatabaseState.Sku.Capacity = dtuCapacity;
            }
        }

        public void SetMaxSizeBytes(long maxSizeBytes)
        {
            if (this.RequestedMsSqlDatabaseState.MaxSizeBytes == null)
            {
                this.RequestedMsSqlDatabaseState.MaxSizeBytes = maxSizeBytes;
                return;
            }

            if (maxSizeBytes < this.RequestedMsSqlDatabaseState.MaxSizeBytes)
            {
                return;
            }

            this.RequestedMsSqlDatabaseState.MaxSizeBytes = maxSizeBytes;
        }

        public override ResourcePatchOperation PreparePatch()
        {
            ResourcePatchOperation result = new ResourcePatchOperation();

            var patch = new MsSqlDatabaseState();

            // Get current values
            long currentStorage = (long)(this.ExistingMsSqlDatabaseState.CurrentUsedStorage ?? 0);

            patch.Sku = this.RequestedMsSqlDatabaseState.Sku.DeepCopy() ?? this.ExistingMsSqlDatabaseState.Sku.DeepCopy();

            patch.MaxSizeBytes = this.RequestedMsSqlDatabaseState.MaxSizeBytes ?? this.ExistingMsSqlDatabaseState.MaxSizeBytes;

            // Normalize MaxSizeBytes - use database-specific validation if it's not an elastic pool database
            var minValidCapacityForCurrentUsage = MsSqlDatabaseResourceStateHelper.FindClosestValidStorageSizeForDatabase(currentStorage, patch.Sku);
            if (minValidCapacityForCurrentUsage > patch.MaxSizeBytes)
            {
                patch.MaxSizeBytes = minValidCapacityForCurrentUsage;
            }
            patch.MaxSizeBytes = MsSqlDatabaseResourceStateHelper.FindClosestValidStorageSizeForDatabase((long)patch.MaxSizeBytes, patch.Sku);

            // This only works for DTU based (except for elasticpool, which is set at the pool level)
            if (MsSqlDatabaseResourceStateHelper.IsDtuModel(patch.Sku) && patch.Sku.Name != "ElasticPool")
            {
                // Ensure Capacity can accomodate storage
                (var targetDtu, var targetMaxDataBytes) = MsSqlDatabaseResourceStateHelper.FindClosestDtuThatCanHoldStorage(patch.Sku, patch.Sku.Capacity.Value, (long)patch.MaxSizeBytes);
                patch.Sku.Capacity = (int)targetDtu;
            }

            bool hasChanges = (patch.Sku != null && patch.Sku.Capacity != this.ExistingMsSqlDatabaseState.Sku.Capacity)
                              || (patch.MaxSizeBytes != null && patch.MaxSizeBytes != this.ExistingMsSqlDatabaseState.MaxSizeBytes);

            result.PatchData = patch;
            result.HasChanges = hasChanges;
            result.Disruptive = hasChanges && patch.Sku?.Capacity != this.ExistingMsSqlDatabaseState.Sku.Capacity;
            return result;
        }

        public override async Task ApplyChanges(ResourcePatchOperation operation, CancellationToken cancellationToken)
        {
            var database = (SqlDatabaseResource)this.Resource;

            if (!(operation.PatchData is MsSqlDatabaseState internalPatch))
            {
                throw new Exception();
            }

            SqlDatabasePatch patch = new SqlDatabasePatch();
            patch.Sku = internalPatch.Sku.DeepCopy();
            patch.MaxSizeBytes = internalPatch.MaxSizeBytes;

            var result = await database.UpdateAsync(Azure.WaitUntil.Completed, patch, cancellationToken);
            this.ValidateArmResult(result);
        }
    }
}