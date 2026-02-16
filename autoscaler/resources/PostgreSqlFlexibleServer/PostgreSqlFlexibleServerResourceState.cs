using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.PostgreSql.FlexibleServers;
using Azure.ResourceManager.PostgreSql.FlexibleServers.Models;
using Microsoft.Extensions.Logging;
using poolautoscaler.configuration;
using poolautoscaler.metrics;
using poolautoscaler.metrics.Dto;
using poolautoscaler.resourcemanagement;
using poolautoscaler.resourcemanagement.Dto;
using poolautoscaler.utils;

namespace poolautoscaler.resources.PostgreSqlFlexibleServer
{
    /// <summary>
    /// Resource state for a PostgreSQL Flexible Server.
    /// </summary>
    public class PostgreSqlFlexibleServerResourceState : ResourceState
    {
        /// <inheritdoc />
        public override object ExistingStateRaw => this.ExistingPostgreSqlFlexibleServerState;

        /// <inheritdoc />
        public override object RequestedStateRaw => this.RequestedPostgreSqlFlexibleServerState;

        /// <summary>Gets or sets the requested server state (SKU, IOPS, core count).</summary>
        public Dto.PostgreSqlFlexibleServerState RequestedPostgreSqlFlexibleServerState { get; set; }

        /// <summary>Gets or sets the current existing server state from Azure.</summary>
        public Dto.PostgreSqlFlexibleServerState ExistingPostgreSqlFlexibleServerState { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="PostgreSqlFlexibleServerResourceState"/> class.
        /// </summary>
        /// <param name="id">The PostgreSQL flexible server resource ID.</param>
        /// <param name="logger">The logger.</param>
        /// <param name="resourceConfiguration">The resource configuration.</param>
        /// <param name="resourceLocationResolver">Optional resource location resolver.</param>
        /// <param name="vmSizeResolver">Optional VM size resolver.</param>
        public PostgreSqlFlexibleServerResourceState(
            string id,
            ILogger logger,
            Resource resourceConfiguration,
            IResourceLocationResolver? resourceLocationResolver = null,
            IVmSizeResolver? vmSizeResolver = null)
            : base(id, logger, resourceConfiguration, resourceLocationResolver, vmSizeResolver)
        {
            if (!ResourceStateFactory.PostgreSqlFlexibleServer.IsMatch(id))
            {
                throw new ArgumentException("Invalid PostgreSQL Flexible Server resource ID", nameof(id));
            }
        }

        /// <summary>Sets the requested IOPS (only increases if already set).</summary>
        /// <param name="iops">The IOPS value as string.</param>
        public void SetIops(string iops)
        {
            int parsedIops = (int)double.Parse(iops);

            if (this.RequestedPostgreSqlFlexibleServerState.Iops == null)
            {
                this.RequestedPostgreSqlFlexibleServerState.Iops = parsedIops;
                return;
            }

            if (parsedIops < this.RequestedPostgreSqlFlexibleServerState.Iops)
            {
                return;
            }

            this.RequestedPostgreSqlFlexibleServerState.Iops = parsedIops;
        }

        /// <summary>Sets the requested SKU name (only increases tier if already set).</summary>
        /// <param name="sku">The SKU name.</param>
        public void SetSku(string sku)
        {
            if (this.RequestedPostgreSqlFlexibleServerState.Sku == null)
            {
                this.RequestedPostgreSqlFlexibleServerState.Sku = this.ExistingPostgreSqlFlexibleServerState.Sku.DeepCopy();
                this.RequestedPostgreSqlFlexibleServerState.Sku.Name = sku;
                return;
            }

            if (PostgreSqlFlexibleServerResourceStateHelper.CompareSku(this.ExistingPostgreSqlFlexibleServerState.Sku.Tier.ToString(), sku, this.RequestedPostgreSqlFlexibleServerState.Sku.Name) <= 0)
            {
                return;
            }

            this.RequestedPostgreSqlFlexibleServerState.Sku.Name = sku;
        }

        /// <summary>Sets the requested core count (only increases if already set).</summary>
        /// <param name="coreCount">The core count as string.</param>
        public void SetCoreCount(string coreCount)
        {
            int intCoreCount = (int)double.Parse(coreCount);

            if (this.RequestedPostgreSqlFlexibleServerState.CoreCount == null)
            {
                this.RequestedPostgreSqlFlexibleServerState.CoreCount = intCoreCount;
                return;
            }

            if (intCoreCount > this.RequestedPostgreSqlFlexibleServerState.CoreCount)
            {
                this.RequestedPostgreSqlFlexibleServerState.CoreCount = intCoreCount;
            }
        }

        /// <inheritdoc />
        public override ResourcePatchOperation PreparePatch()
        {
            ResourcePatchOperation operation = new ResourcePatchOperation();

            var patch = new Dto.PostgreSqlFlexibleServerState();

            var activeTier = this.ExistingPostgreSqlFlexibleServerState.Sku.Tier.ToString();

            var requestedSku = this.ExistingPostgreSqlFlexibleServerState.Sku.DeepCopy();

            requestedSku.Name = (from p in PostgreSqlFlexibleServerResourceStateHelper.AllSkus
                                 where p.Tier == activeTier
                                 orderby p.MaxIops
                                 select p).First().Sku;

            if (this.RequestedPostgreSqlFlexibleServerState.Sku != null)
            {
                requestedSku = this.RequestedPostgreSqlFlexibleServerState.Sku;
            }

            if (this.RequestedPostgreSqlFlexibleServerState.CoreCount > 0)
            {
                var minSkuForRequestedCoreCount =
                    PostgreSqlFlexibleServerResourceStateHelper.GetMinimumSkuThatSatisfiesCoreCount(
                        this.RequestedPostgreSqlFlexibleServerState.CoreCount.Value, activeTier);

                if (PostgreSqlFlexibleServerResourceStateHelper.CompareSku(
                        activeTier,
                        minSkuForRequestedCoreCount,
                        requestedSku.Name) > 0)
                {
                    requestedSku = new PostgreSqlFlexibleServerSku(minSkuForRequestedCoreCount, activeTier);
                }
            }

            if (this.RequestedPostgreSqlFlexibleServerState.Iops != null)
            {
                var minSkuForIops =
                    PostgreSqlFlexibleServerResourceStateHelper.GetMinimumSkuThatSatisfiesIops(
                        this.RequestedPostgreSqlFlexibleServerState.Iops.Value, activeTier);

                if (PostgreSqlFlexibleServerResourceStateHelper.CompareSku(
                        activeTier,
                        minSkuForIops.Sku,
                        requestedSku.Name) > 0)
                {
                    requestedSku.Name = minSkuForIops.Sku;
                }

                patch.Iops = (int)Math.Ceiling(this.RequestedPostgreSqlFlexibleServerState.Iops.Value / 50.0) * 50;

                var requestedSkuInfo = PostgreSqlFlexibleServerResourceStateHelper.GetSkuInfo(requestedSku.Name);

                if (patch.Iops < requestedSkuInfo.MinIops)
                {
                    patch.Iops = requestedSkuInfo.MinIops;
                }
            }

            patch.Sku = requestedSku;

            bool hasChanges = (patch.Sku != null && patch.Sku.Name != this.ExistingPostgreSqlFlexibleServerState.Sku.Name)
                || (patch.Iops != null && patch.Iops != this.ExistingPostgreSqlFlexibleServerState.Iops);

            operation.PatchData = patch;
            operation.HasChanges = hasChanges;
            operation.Disruptive = hasChanges && patch.Sku?.Name != this.ExistingPostgreSqlFlexibleServerState.Sku.Name;

            return operation;
        }

        /// <inheritdoc />
        public override async Task ApplyChanges(ResourcePatchOperation operation, CancellationToken cancellationToken)
        {
            var server = this.ResourceCasted;

            if (!(operation.PatchData is Dto.PostgreSqlFlexibleServerState internalPatch))
            {
                throw new ArgumentException();
            }

            PostgreSqlFlexibleServerPatch patch = new PostgreSqlFlexibleServerPatch();

            patch.Sku = internalPatch.Sku;

            if (internalPatch.Iops.HasValue && server.Data.Storage != null)
            {
                patch.Storage = server.Data.Storage.DeepCopy();
                patch.Storage.Iops = internalPatch.Iops;
            }

            var result = await server.UpdateAsync(Azure.WaitUntil.Completed, patch, cancellationToken);
            this.ValidateArmResult(result);
        }

        /// <inheritdoc />
        public override async Task<MetricEvalDtoResult> CustomMetric(
            ArmClient client,
            TokenCredential credential,
            CancellationToken cancellationToken,
            ScalingConfiguration setting,
            string name)
        {
            switch (name)
            {
                default:
                    throw new NotImplementedException("Custom metric not implemented: " + name);
            }
        }

        /// <inheritdoc />
        protected override async Task InternalRefreshAsync(ArmClient client, TokenCredential credential, CancellationToken cancellationToken)
        {
            this.Resource = await client.GetPostgreSqlFlexibleServerResource(new ResourceIdentifier(this.ResourceId)).GetAsync(cancellationToken);
            this.Location = this.ResourceCasted.Data.Location;
            this.PopulateResourceTags(this.ResourceCasted.Data.Tags);
            this.ExistingPostgreSqlFlexibleServerState = new Dto.PostgreSqlFlexibleServerState()
            {
                Sku = this.ResourceCasted.Data.Sku,
                CoreCount = PostgreSqlFlexibleServerResourceStateHelper.GetCoreCountFromSkuName(this.ResourceCasted.Data.Sku.Name),
                Iops = this.ResourceCasted.Data.Storage?.Iops
            };
            this.RequestedPostgreSqlFlexibleServerState = new Dto.PostgreSqlFlexibleServerState();
        }

        private PostgreSqlFlexibleServerResource? ResourceCasted { get => this.Resource as PostgreSqlFlexibleServerResource; }
    }
}
