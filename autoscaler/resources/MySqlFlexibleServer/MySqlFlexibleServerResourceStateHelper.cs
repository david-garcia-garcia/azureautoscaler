using System.Text.RegularExpressions;
using Azure.ResourceManager;
using Azure.ResourceManager.MySql.FlexibleServers;
using poolautoscaler.resources.MySqlFlexibleServer.Dto;
using poolautoscaler.utils;

namespace poolautoscaler.resources.MySqlFlexibleServer
{
    public static class MySqlFlexibleServerResourceStateHelper
    {
        public static readonly SkuInfo[] AllSkus =
        [
            new SkuInfo("Burstable", "Standard_B1ms", 360, 640),
            new SkuInfo("Burstable", "Standard_B2s", 360, 1280),
            new SkuInfo("Burstable", "Standard_B2ms", 360, 1700),
            new SkuInfo("Burstable", "Standard_B4ms", 360, 2400),
            new SkuInfo("Burstable", "Standard_B8ms", 360, 3100),
            new SkuInfo("Burstable", "Standard_B12ms", 360, 3800),
            new SkuInfo("Burstable", "Standard_B16ms", 360, 4300),
            new SkuInfo("Burstable", "Standard_B20ms", 360, 5000),
            new SkuInfo("GeneralPurpose", "Standard_D2ds_v4", 360, 3200),
            new SkuInfo("GeneralPurpose", "Standard_D4ds_v4", 360, 6400),
            new SkuInfo("GeneralPurpose", "Standard_D8ds_v4", 360, 12800),
            new SkuInfo("GeneralPurpose", "Standard_D16ds_v4", 360, 20000),
            new SkuInfo("GeneralPurpose", "Standard_D32ds_v4", 360, 20000),
            new SkuInfo("GeneralPurpose", "Standard_D48ds_v4", 360, 48000),
            new SkuInfo("GeneralPurpose", "Standard_D64ds_v4", 360, 48000),
            new SkuInfo("GeneralPurpose", "Standard_D2ads_v5", 360, 3200),
            new SkuInfo("GeneralPurpose", "Standard_D4ads_v5", 360, 6400),
            new SkuInfo("GeneralPurpose", "Standard_D8ads_v5", 360, 12800),
            new SkuInfo("GeneralPurpose", "Standard_D16ads_v5", 360, 20000),
            new SkuInfo("GeneralPurpose", "Standard_D32ads_v5", 360, 20000),
            new SkuInfo("GeneralPurpose", "Standard_D48ads_v5", 360, 48000),
            new SkuInfo("GeneralPurpose", "Standard_D64ads_v5", 360, 48000),
            new SkuInfo("GeneralPurpose", "Standard_D96ads_v5", 360, 48000),
            new SkuInfo("BusinessCritical", "Standard_E2ds_v5", 360, 5000),
            new SkuInfo("BusinessCritical", "Standard_E4ds_v5", 360, 10000),
            new SkuInfo("BusinessCritical", "Standard_E8ds_v5", 360, 18000),
            new SkuInfo("BusinessCritical", "Standard_E16ds_v5", 360, 28800),
            new SkuInfo("BusinessCritical", "Standard_E20ds_v5", 360, 28800),
            new SkuInfo("BusinessCritical", "Standard_E32ds_v5", 360, 38000),
            new SkuInfo("BusinessCritical", "Standard_E48ds_v5", 360, 48000),
            new SkuInfo("BusinessCritical", "Standard_E64ds_v5", 360, 64000),
            new SkuInfo("BusinessCritical", "Standard_E96ds_v5", 360, 80000)
        ];

        public static string[] GetCapacityValues(ArmResource resource)
        {
            if (!(resource is MySqlFlexibleServerResource server))
            {
                throw new ArgumentException("Resource is not a MySqlFlexibleServerResource.");
            }

            return GetCapacityValues(server.Data.Sku.Tier.ToString());
        }

        public static SkuInfo? GetSkuInfo(string Sku)
        {
            return (from p in AllSkus where p.Sku == Sku select p).FirstOrDefault();
        }

        public static string[] GetCapacityValues(string tier)
        {
            switch (tier)
            {
                case "Burstable": return AllSkus.Where(s => s.Tier == "Burstable").Select(s => s.Sku).ToArray();
                case "BusinessCritical": return AllSkus.Where(s => s.Tier == "BusinessCritical").Select(s => s.Sku).ToArray();
                case "GeneralPurpose": return AllSkus.Where(s => s.Tier == "GeneralPurpose").Select(s => s.Sku).ToArray();
                default: throw new ArgumentException("Invalid SKU: " + tier);
            }
        }

        public static int GetCoreCountFromSkuName(string sku)
        {
            return int.Parse(Regex.Match(sku, @"[A-Z](\d+)").Groups[1].Value);
        }

        public static string GetMinimumSkuThatSatisfiesCoreCount(int coreCount, string tier, int millicoresPerHour = 0)
        {
            var capacities = GetCapacityValues(tier);
            foreach (var capacity in capacities)
            {
                var cores = GetCoreCountFromSkuName(capacity);
                var credits = BurstableVmInfo.GetBurstableCreditsPerHour(capacity);
                var baseLine = BurstableVmInfo.GetBurstableBaselinePerformance(capacity);
                if (cores < coreCount)
                {
                    continue;
                }

                var forecastedCpuAverageUsage = (millicoresPerHour / (cores * 10));
                if (forecastedCpuAverageUsage > 0)
                {
                    var consumedCredits = cores * ((forecastedCpuAverageUsage - baseLine) / 100) * 60;
                    if (consumedCredits > (credits * 0.8))
                    {
                        continue;
                    }
                }

                return capacity;
            }

            throw new Exception($"Could not find a suitable SKU to accomodate {coreCount} cores and {millicoresPerHour} burstable_credits/core ");
        }

        public static SkuInfo GetMinimumSkuThatSatisfiesIops(int iops, string? tier = null)
        {
            var skus = tier == null ? AllSkus : AllSkus.Where(s => s.Tier == tier).ToArray();
            var suitableSkus = skus.Where(s => s.MaxIops >= iops).ToArray();
            if (!suitableSkus.Any())
            {
                throw new Exception($"Could not find a suitable SKU to accommodate {iops} IOPS" + (tier != null ? $" for tier {tier}" : ""));
            }

            return suitableSkus.OrderBy(s => s.MaxIops).First();
        }

        public static int CompareSku(string tier, string dimensionValue1, string dimensionValue2)
        {
            ArgumentException.ThrowIfNullOrEmpty(tier);
            ArgumentException.ThrowIfNullOrEmpty(dimensionValue1);
            ArgumentException.ThrowIfNullOrEmpty(dimensionValue2);
            var capacityValues = GetCapacityValues(tier);
            var pos1 = Array.IndexOf(capacityValues, dimensionValue1);
            var pos2 = Array.IndexOf(capacityValues, dimensionValue2);
            if (pos1 != -1 && pos2 != -1)
            {
                return pos1.CompareTo(pos2);
            }

            throw new ArgumentException($"Invalid dimension values. Value1: '{dimensionValue1}', Value2: '{dimensionValue2}', Tier: '{tier}'");
        }
    }
}
