using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.MySql.FlexibleServers;
using Azure.ResourceManager.MySql.FlexibleServers.Models;
using Microsoft.Extensions.Logging;
using poolautoscaler.strategies;
using poolautoscaler.utils;

namespace poolautoscaler.resources
{
    public class MySqlFlexibleServerResourceState : ResourceState
    {
        public class MySqlFlexibleServerState
        {
            // Dtu request
            public MySqlFlexibleServerSku? Sku { get; set; }

            // Max size request
            public int? CoreCount { get; set; }

            public int? Iops { get; set; }
        }

        public override object ExistingStateRaw => this.ExistingMySqlFlexibleServerState;

        public override object RequestedStateRaw => this.RequestedMySqlFlexibleServerState;

        public MySqlFlexibleServerState RequestedMySqlFlexibleServerState { get; set; }
        public MySqlFlexibleServerState ExistingMySqlFlexibleServerState { get; set; }

        private Dictionary<string, CapacityForecast> Forecasts = new Dictionary<string, CapacityForecast>();

        private new MySqlFlexibleServerResource? ResourceCasted { get => this.Resource as MySqlFlexibleServerResource; }

        public MySqlFlexibleServerResourceState(string id, ILogger logger, Resource resourceConfiguration) : base(id, logger, resourceConfiguration)
        {
            if (!ResourceStateFactory.MySqlFlexibleServer.IsMatch(id))
            {
                throw new ArgumentException("Invalid MySQL Flexible Server resource ID", nameof(id));
            }
        }

        protected override async Task InternalRefreshAsync(ArmClient client, TokenCredential credential, CancellationToken cancellationToken)
        {
            this.Resource = await client.GetMySqlFlexibleServerResource(new ResourceIdentifier(this.ResourceId)).GetAsync(cancellationToken);

            // Populate resource tags
            this.PopulateResourceTags(this.ResourceCasted.Data.Tags);

            this.ExistingMySqlFlexibleServerState = new MySqlFlexibleServerState()
            {
                Sku = this.ResourceCasted.Data.Sku,
                CoreCount = MySqlFlexibleServerResourceStateHelper.GetCoreCountFromSkuName(this.ResourceCasted.Data.Sku.Name),
                Iops = this.ResourceCasted.Data.Storage.Iops
            };

            this.RequestedMySqlFlexibleServerState = new MySqlFlexibleServerState();
        }

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
                // Do not ddowngrade a request
                return;
            }

            this.RequestedMySqlFlexibleServerState.Iops = parsedIops;
        }

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
                // Do not ddowngrade a request
                return;
            }

            this.RequestedMySqlFlexibleServerState.Sku.Name = sku;
        }

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

        public override ResourcePatchOperation PreparePatch()
        {
            ResourcePatchOperation operation = new ResourcePatchOperation();

            var patch = new MySqlFlexibleServerState();

            // We currently NOT support changes between tiers
            var activeTier = this.ExistingMySqlFlexibleServerState.Sku.Tier.ToString();

            // Initialize requested SKU with the lowest SKU possible in the existing tier
            var requestedSku = this.ExistingMySqlFlexibleServerState.Sku.DeepCopy();

            requestedSku.Name = (from p in MySqlFlexibleServerResourceStateHelper.AllSkus
                                 where p.Tier == activeTier
                                 orderby p.MaxIops
                                 select p).First().Sku;

            if (this.RequestedMySqlFlexibleServerState.Sku != null)
            {
                requestedSku = this.RequestedMySqlFlexibleServerState.Sku;
            }

            // If a core count was requested, use that to calculate or override whatever SKU was requested
            if (this.RequestedMySqlFlexibleServerState.CoreCount > 0)
            {
                var minSkuForRequestedCoreCount =
                    MySqlFlexibleServerResourceStateHelper.GetMinimumSkuThatSatisfiesCoreCount(
                        this.RequestedMySqlFlexibleServerState.CoreCount.Value, activeTier);

                if (MySqlFlexibleServerResourceStateHelper.CompareSku(activeTier,
                        minSkuForRequestedCoreCount, requestedSku.Name) > 0)
                {
                    requestedSku = new MySqlFlexibleServerSku(minSkuForRequestedCoreCount, activeTier);
                }
            }

            if (this.RequestedMySqlFlexibleServerState.Iops != null)
            {
                // Adjust the IOPS within the target SKU, if it does not fit, a new higher SKU might be provisioned.
                var minSkuForIops =
                    MySqlFlexibleServerResourceStateHelper.GetMinimumSkuThatSatisfiesIops(
                        this.RequestedMySqlFlexibleServerState.Iops.Value, activeTier);

                if (MySqlFlexibleServerResourceStateHelper.CompareSku(activeTier,
                        minSkuForIops.Sku, requestedSku.Name) > 0)
                {
                    requestedSku.Name = minSkuForIops.Sku;
                }

                // Increments of 50 to avoid small changes triggering a resource modification
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

        public override async Task ApplyChanges(ResourcePatchOperation operation, CancellationToken cancellationToken)
        {
            var server = this.ResourceCasted;

            if (!(operation.PatchData is MySqlFlexibleServerState internalPatch))
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
                    break;
                default:
                    throw new NotImplementedException("Custom metric not implemented: " + name);
            }
            return null;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="client"></param>
        /// <param name="credential"></param>
        /// <param name="cancellationToken"></param>
        /// <param name="setting"></param>
        /// <param name="name"></param>
        /// <returns></returns>
        private async Task EnsureCustomCoreCountForecast(ArmClient client,
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
    }
}