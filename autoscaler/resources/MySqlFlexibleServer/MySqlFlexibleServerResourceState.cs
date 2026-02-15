using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.MySql.FlexibleServers;
using Azure.ResourceManager.MySql.FlexibleServers.Models;
using Microsoft.Extensions.Logging;
using poolautoscaler.configuration;
using poolautoscaler.metrics.Dto;
using poolautoscaler.resourcemanagement;
using poolautoscaler.resourcemanagement.Dto;
using poolautoscaler.resources.MySqlFlexibleServer.Dto;
using poolautoscaler.utils;

namespace poolautoscaler.resources.MySqlFlexibleServer
{
    /// <summary>
    /// Resource state for a MySQL Flexible Server.
    /// </summary>
    public class MySqlFlexibleServerResourceState : ResourceState
    {
        /// <inheritdoc />
        public override object ExistingStateRaw => this.ExistingMySqlFlexibleServerState;

        /// <inheritdoc />
        public override object RequestedStateRaw => this.RequestedMySqlFlexibleServerState;

        /// <summary>Gets or sets the requested server state (SKU, IOPS, core count).</summary>
        public Dto.MySqlFlexibleServerState RequestedMySqlFlexibleServerState { get; set; }

        /// <summary>Gets or sets the current existing server state from Azure.</summary>
        public Dto.MySqlFlexibleServerState ExistingMySqlFlexibleServerState { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="MySqlFlexibleServerResourceState"/> class.
        /// </summary>
        /// <param name="id">The MySQL flexible server resource ID.</param>
        /// <param name="logger">The logger.</param>
        /// <param name="resourceConfiguration">The resource configuration.</param>
        public MySqlFlexibleServerResourceState(string id, ILogger logger, Resource resourceConfiguration) : base(id, logger, resourceConfiguration)
        {
            if (!ResourceStateFactory.MySqlFlexibleServer.IsMatch(id))
            {
                throw new ArgumentException("Invalid MySQL Flexible Server resource ID", nameof(id));
            }
        }

        /// <summary>Sets the requested IOPS (only increases if already set).</summary>
        /// <param name="iops">The IOPS value as string.</param>
        public void SetIops(string iops)
        {
            int parsedIops = (int)double.Parse(iops);

            if (this.RequestedMySqlFlexibleServerState.Iops == null)
            {
                this.RequestedMySqlFlexibleServerState.Iops = parsedIops;
                return;
            }

            if (parsedIops < this.RequestedMySqlFlexibleServerState.Iops)
            {
                return;
            }

            this.RequestedMySqlFlexibleServerState.Iops = parsedIops;
        }

        /// <summary>Sets the requested SKU name (only increases tier if already set).</summary>
        /// <param name="sku">The SKU name.</param>
        public void SetSku(string sku)
        {
            if (this.RequestedMySqlFlexibleServerState.Sku == null)
            {
                this.RequestedMySqlFlexibleServerState.Sku = this.ExistingMySqlFlexibleServerState.Sku.DeepCopy();
                this.RequestedMySqlFlexibleServerState.Sku.Name = sku;
                return;
            }

            if (MySqlFlexibleServerResourceStateHelper.CompareSku(this.ExistingMySqlFlexibleServerState.Sku.Tier.ToString(), sku, this.RequestedMySqlFlexibleServerState.Sku.Name) <= 0)
            {
                return;
            }

            this.RequestedMySqlFlexibleServerState.Sku.Name = sku;
        }

        /// <summary>Sets the requested core count (only increases if already set).</summary>
        /// <param name="coreCount">The core count as string.</param>
        public void SetCoreCount(string coreCount)
        {
            int intCoreCount = (int)double.Parse(coreCount);

            if (this.RequestedMySqlFlexibleServerState.CoreCount == null)
            {
                this.RequestedMySqlFlexibleServerState.CoreCount = intCoreCount;
                return;
            }

            if (intCoreCount > this.RequestedMySqlFlexibleServerState.CoreCount)
            {
                this.RequestedMySqlFlexibleServerState.CoreCount = intCoreCount;
            }
        }

        /// <inheritdoc />
        public override ResourcePatchOperation PreparePatch()
        {
            ResourcePatchOperation operation = new ResourcePatchOperation();

            var patch = new Dto.MySqlFlexibleServerState();

            var activeTier = this.ExistingMySqlFlexibleServerState.Sku.Tier.ToString();

            var requestedSku = this.ExistingMySqlFlexibleServerState.Sku.DeepCopy();

            requestedSku.Name = (from p in MySqlFlexibleServerResourceStateHelper.AllSkus
                                 where p.Tier == activeTier
                                 orderby p.MaxIops
                                 select p).First().Sku;

            if (this.RequestedMySqlFlexibleServerState.Sku != null)
            {
                requestedSku = this.RequestedMySqlFlexibleServerState.Sku;
            }

            if (this.RequestedMySqlFlexibleServerState.CoreCount > 0)
            {
                var minSkuForRequestedCoreCount =
                    MySqlFlexibleServerResourceStateHelper.GetMinimumSkuThatSatisfiesCoreCount(
                        this.RequestedMySqlFlexibleServerState.CoreCount.Value, activeTier);

                if (MySqlFlexibleServerResourceStateHelper.CompareSku(
                        activeTier,
                        minSkuForRequestedCoreCount,
                        requestedSku.Name) > 0)
                {
                    requestedSku = new MySqlFlexibleServerSku(minSkuForRequestedCoreCount, activeTier);
                }
            }

            if (this.RequestedMySqlFlexibleServerState.Iops != null)
            {
                var minSkuForIops =
                    MySqlFlexibleServerResourceStateHelper.GetMinimumSkuThatSatisfiesIops(
                        this.RequestedMySqlFlexibleServerState.Iops.Value, activeTier);

                if (MySqlFlexibleServerResourceStateHelper.CompareSku(
                        activeTier,
                        minSkuForIops.Sku,
                        requestedSku.Name) > 0)
                {
                    requestedSku.Name = minSkuForIops.Sku;
                }

                patch.Iops = (int)Math.Ceiling(this.RequestedMySqlFlexibleServerState.Iops.Value / 50.0) * 50;

                var requestedSkuInfo = MySqlFlexibleServerResourceStateHelper.GetSkuInfo(requestedSku.Name);

                if (patch.Iops < requestedSkuInfo.MinIops)
                {
                    patch.Iops = requestedSkuInfo.MinIops;
                }
            }

            patch.Sku = requestedSku;

            bool hasChanges = (patch.Sku != null && patch.Sku.Name != this.ExistingMySqlFlexibleServerState.Sku.Name)
                || (patch.Iops != null && patch.Iops != this.ExistingMySqlFlexibleServerState.Iops);

            operation.PatchData = patch;
            operation.HasChanges = hasChanges;
            operation.Disruptive = hasChanges && patch.Sku?.Name != this.ExistingMySqlFlexibleServerState.Sku.Name;

            return operation;
        }

        /// <inheritdoc />
        public override async Task ApplyChanges(ResourcePatchOperation operation, CancellationToken cancellationToken)
        {
            var server = this.ResourceCasted;

            if (!(operation.PatchData is Dto.MySqlFlexibleServerState internalPatch))
            {
                throw new ArgumentException();
            }

            MySqlFlexibleServerPatch patch = new MySqlFlexibleServerPatch();

            patch.Sku = internalPatch.Sku;

            if (internalPatch.Iops.HasValue)
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
                case "custom_sku_corecount_forecast":
                    await this.EnsureCustomCoreCountForecast(client, credential, cancellationToken, setting);
                    return new MetricEvalDtoResult()
                    {
                        Values = new List<MetricEvalDtoResultValue>()
                        {
                            new MetricEvalDtoResultValue()
                            {
                                CustomString = this.Forecasts[setting.Id].ForecastString[DateTime.UtcNow.DayOfWeek]
                            }
                        }
                    };
                default:
                    throw new NotImplementedException("Custom metric not implemented: " + name);
            }
        }

        /// <inheritdoc />
        protected override async Task InternalRefreshAsync(ArmClient client, TokenCredential credential, CancellationToken cancellationToken)
        {
            this.Resource = await client.GetMySqlFlexibleServerResource(new ResourceIdentifier(this.ResourceId)).GetAsync(cancellationToken);
            this.PopulateResourceTags(this.ResourceCasted.Data.Tags);
            this.ExistingMySqlFlexibleServerState = new Dto.MySqlFlexibleServerState()
            {
                Sku = this.ResourceCasted.Data.Sku,
                CoreCount = MySqlFlexibleServerResourceStateHelper.GetCoreCountFromSkuName(this.ResourceCasted.Data.Sku.Name),
                Iops = this.ResourceCasted.Data.Storage.Iops
            };
            this.RequestedMySqlFlexibleServerState = new Dto.MySqlFlexibleServerState();
        }

        private async Task EnsureCustomCoreCountForecast(
            ArmClient client,
            TokenCredential credential,
            CancellationToken cancellationToken,
            ScalingConfiguration setting)
        {
            if (this.Forecasts.ContainsKey(setting.Id))
            {
                if (this.Forecasts[setting.Id].ExpiresAt < DateTime.UtcNow)
                {
                    this.Forecasts.Remove(setting.Id);
                }
                else
                {
                    return;
                }
            }

            var forecaster = new MySqlFlexibleServerCoreCountForecast(this.ResourceId, this.Logger, this);
            var f = await forecaster.CustomCoreCountForecast(client, credential, cancellationToken, setting);
            this.Forecasts.Add(setting.Id, f);
        }

        private MySqlFlexibleServerResource? ResourceCasted { get => this.Resource as MySqlFlexibleServerResource; }

        private Dictionary<string, CapacityForecast> Forecasts = new Dictionary<string, CapacityForecast>();
    }
}
