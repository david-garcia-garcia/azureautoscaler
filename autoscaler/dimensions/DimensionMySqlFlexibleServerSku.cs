using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.MySql.FlexibleServers;
using Microsoft.Extensions.Logging;
using poolautoscaler.resources;

namespace poolautoscaler.dimensions
{
    internal class DimensionMySqlFlexibleServerSku : IDimension
    {


        public DimensionMySqlFlexibleServerSku()
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
                && rule.Dimension == "Sku")
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
            if (MySqlFlexibleServerResourceStateHelper.AllSkus.Any(s => s.Sku == value))
            {
                return;
            }

            throw new ArgumentException("DimensionMySqlFlexibleServerSku value not supported.");
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
            return MySqlFlexibleServerResourceStateHelper.CompareSku((resource as MySqlFlexibleServerResource).Data.Sku.Tier.ToString(), dimensionValue1, dimensionValue2);
        }

        public string GetCurrentDimensionValue(ResourceState resource)
        {
            if (!(resource.Resource is MySqlFlexibleServerResource sqlDatabase))
            {
                throw new ArgumentException("Resource is not a MySqlFlexibleServerResource.");
            }

            return sqlDatabase.Data.Sku.Name.ToString()!;
        }

        public string GetNextDimensionValue(ResourceState resource, string value)
        {
            var capacityValues = MySqlFlexibleServerResourceStateHelper.GetCapacityValues(resource.Resource);

            int position = Array.IndexOf(capacityValues, value);

            if (position < capacityValues.Length - 1)
            {
                return capacityValues[position + 1].ToString();
            }

            // Already maxed out
            return value;
        }

        public string GetPreviousDimensionValue(ResourceState resource, string value)
        {
            var capacityValues = MySqlFlexibleServerResourceStateHelper.GetCapacityValues(resource.Resource);

            int position = Array.IndexOf(capacityValues, value);

            if (position == 0)
            {
                // If current one is the lowest, then this is the target.
                return value;
            }

            return capacityValues[position - 1].ToString();
        }

        public string? GetRequestedDimensionValue(ResourceState resource)
        {
            if (!(resource is MySqlFlexibleServerResourceState mysqlState))
            {
                throw new ArgumentException($"Resource is not {nameof(MySqlFlexibleServerResourceState)}.");
            }

            return mysqlState.RequestedMySqlFlexibleServerState?.Sku?.Name;
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

            (resource as MySqlFlexibleServerResourceState).SetSku(value);
        }
    }
}
