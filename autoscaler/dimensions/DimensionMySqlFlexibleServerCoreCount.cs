using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.MySql.FlexibleServers;
using Microsoft.Extensions.Logging;
using poolautoscaler.resources;

namespace poolautoscaler.dimensions
{
    internal class DimensionMySqlFlexibleServerCoreCount : IDimension
    {


        public DimensionMySqlFlexibleServerCoreCount()
        {
        }

        /// <summary>
        /// Check that this rule can be applied to the given resource.
        /// </summary>
        /// <param name="resource"></param>
        /// <param name="rule"></param>
        /// <returns></returns>
        public bool CanApplyDimension(ResourceState resource, ScalingRule rule, ILogger logger)
        {
            if (resource.Resource is MySqlFlexibleServerResource
                && rule.Dimension == "CoreCount")
            {
                return true;
            }

            return false;
        }

        public void ValidateRuleConfiguration(ScalingRule rule)
        {
            //this.ValidateDimensionValue(rule.DimensionValueMin);
            //this.ValidateDimensionValue(rule.DimensionValueMax);
            //this.ValidateDimensionValue(rule.DimensionValue);
        }

        private void ValidateDimensionValue(string value)
        {
            if (!float.TryParse(value, out _))
            {
                throw new Exception("Invalid core count value {value}");
            }
        }

        /// <summary>
        /// Compare two dimension values
        /// </summary>
        /// <param name="dimensionValue1"></param>
        /// <param name="dimensionValue2"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public int Compare(ArmResource resource, string dimensionValue1, string dimensionValue2)
        {
            var value1 = float.Parse(dimensionValue1);
            var value2 = float.Parse(dimensionValue2);
            return value1.CompareTo(value2);
        }

        public string GetCurrentDimensionValue(ResourceState resource)
        {
            if (!(resource.Resource is MySqlFlexibleServerResource sqlDatabase))
            {
                throw new ArgumentException("Resource is not a MySqlFlexibleServerResource.");
            }

            return MySqlFlexibleServerResourceStateHelper.GetCoreCountFromSkuName(sqlDatabase.Data.Sku.Name).ToString();
        }

        public string GetNextDimensionValue(ResourceState resource, string value)
        {
            var current = int.Parse(value);
            return (current + 1).ToString();
        }

        public string GetPreviousDimensionValue(ResourceState resource, string value)
        {
            var current = int.Parse(value);
            var result = current + 1;

            if (result < 1)
            {
                result = 1;
            }

            return result.ToString();
        }

        public string? GetRequestedDimensionValue(ResourceState resource)
        {
            if (!(resource is MySqlFlexibleServerResourceState mysqlState))
            {
                throw new ArgumentException($"Resource is not {nameof(MySqlFlexibleServerResourceState)}.");
            }

            return mysqlState.RequestedMySqlFlexibleServerState?.CoreCount?.ToString();
        }

        public async Task SetDimensionValue(
            CancellationToken stoppingToken,
            ResourceState resource,
            ILogger logger,
            TokenCredential credential,
            string value)
        {
            if (!(resource.Resource is MySqlFlexibleServerResource elasticPool))
            {
                throw new ArgumentException("Resource is not a MySqlFlexibleServerResource.");
            }

            ValidateDimensionValue(value);

            (resource as MySqlFlexibleServerResourceState).SetCoreCount(value);
        }
    }
}
