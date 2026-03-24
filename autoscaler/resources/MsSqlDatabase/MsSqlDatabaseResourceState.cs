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
using poolautoscaler.resources.MsSqlDatabase.Dto;
using poolautoscaler.utils;

namespace poolautoscaler.resources.MsSqlDatabase
{
    /// <summary>
    /// Resource state for an Azure SQL Database.
    /// </summary>
    public class MsSqlDatabaseResourceState : ResourceState
    {
        /// <inheritdoc />
        public override object ExistingStateRaw => this.ExistingMsSqlDatabaseState;

        /// <inheritdoc />
        public override object RequestedStateRaw => this.RequestedMsSqlDatabaseState;

        /// <summary>
        /// Gets or sets the requested database state (SKU, max size).
        /// </summary>
        public MsSqlDatabaseState RequestedMsSqlDatabaseState { get; set; }

        /// <summary>
        /// Gets or sets the current existing database state from Azure.
        /// </summary>
        public MsSqlDatabaseState ExistingMsSqlDatabaseState { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="MsSqlDatabaseResourceState"/> class.
        /// </summary>
        /// <param name="id">The SQL database resource ID.</param>
        /// <param name="logger">The logger.</param>
        /// <param name="resourceConfiguration">The resource configuration.</param>
        /// <param name="resourceLocationResolver">Optional resource location resolver.</param>
        /// <param name="vmSizeResolver">Optional VM size resolver.</param>
        public MsSqlDatabaseResourceState(
            string id,
            ILogger logger,
            Resource resourceConfiguration,
            IResourceLocationResolver? resourceLocationResolver = null,
            IVmSizeResolver? vmSizeResolver = null)
            : base(id, logger, resourceConfiguration, resourceLocationResolver, vmSizeResolver)
        {
            if (!ResourceStateFactory.SqlDatabase.IsMatch(id))
            {
                throw new ArgumentException("Invalid SQL Database resource ID", nameof(id));
            }
        }

        /// <summary>
        /// Sets the requested DTU capacity (only increases if already set).
        /// </summary>
        /// <param name="dtuCapacity">The DTU capacity.</param>
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

        /// <summary>
        /// Sets the requested max size in bytes (only increases if already set).
        /// </summary>
        /// <param name="maxSizeBytes">The maximum size in bytes.</param>
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

        /// <inheritdoc />
        public override ResourcePatchOperation PreparePatch()
        {
            ResourcePatchOperation result = new ResourcePatchOperation();

            var patch = new MsSqlDatabaseState();

            long currentStorage = (long)(this.ExistingMsSqlDatabaseState.CurrentUsedStorage ?? 0);

            patch.Sku = this.RequestedMsSqlDatabaseState.Sku.DeepCopy() ?? this.ExistingMsSqlDatabaseState.Sku.DeepCopy();

            patch.MaxSizeBytes = this.RequestedMsSqlDatabaseState.MaxSizeBytes ?? this.ExistingMsSqlDatabaseState.MaxSizeBytes;

            var minValidCapacityForCurrentUsage = MsSqlDatabaseResourceStateHelper.FindClosestValidStorageSizeForDatabase(currentStorage, patch.Sku);
            if (minValidCapacityForCurrentUsage > patch.MaxSizeBytes)
            {
                patch.MaxSizeBytes = minValidCapacityForCurrentUsage;
            }

            patch.MaxSizeBytes = MsSqlDatabaseResourceStateHelper.FindClosestValidStorageSizeForDatabase((long)patch.MaxSizeBytes, patch.Sku);

            if (MsSqlDatabaseResourceStateHelper.IsDtuModel(patch.Sku) && patch.Sku.Name != "ElasticPool")
            {
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

        /// <inheritdoc />
        public override async Task ApplyChanges(ResourcePatchOperation operation, CancellationToken cancellationToken)
        {
            var database = (SqlDatabaseResource)this.Resource;

            if (!(operation.PatchData is MsSqlDatabaseState internalPatch))
            {
                throw new ArgumentException(
                    $"Patch data must be {nameof(MsSqlDatabaseState)}; actual type was '{operation.PatchData?.GetType().FullName ?? "null"}'.");
            }

            SqlDatabasePatch patch = new SqlDatabasePatch();
            patch.Sku = internalPatch.Sku.DeepCopy();
            patch.MaxSizeBytes = internalPatch.MaxSizeBytes;

            var result = await database.UpdateAsync(Azure.WaitUntil.Completed, patch, cancellationToken);
            this.ValidateArmResult(result);
        }

        /// <inheritdoc />
        protected override async Task InternalRefreshAsync(ArmClient client, TokenCredential credential, CancellationToken cancellationToken)
        {
            this.Resource = await client.GetSqlDatabaseResource(new ResourceIdentifier(this.ResourceId)).GetAsync(cancellationToken);

            var database = (SqlDatabaseResource)this.Resource;

            var metricsClient = new MetricsQueryClient(credential);
            MetricEvaluation eval = new MetricEvaluation(this.Logger);

            var metricResult = eval.RetrieveHistory(metricsClient, this.ResourceId, "storage", TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1), cancellationToken, null, null).Result;
            var values = metricResult.Values;
            values.Reverse();

            var storage_used = values.Take(1)?.Select((i) => i.Average).Average();

            this.ResourceTagsPopulate(database.Data.Tags);

            this.RequestedMsSqlDatabaseState = new MsSqlDatabaseState();

            this.ExistingMsSqlDatabaseState = new MsSqlDatabaseState()
            {
                Sku = database.Data.Sku,
                MaxSizeBytes = database.Data.MaxSizeBytes,
                CurrentUsedStorage = storage_used
            };
        }
    }
}
