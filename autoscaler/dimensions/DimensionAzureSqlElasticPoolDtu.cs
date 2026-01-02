using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.Sql;
using Microsoft.Extensions.Logging;
using poolautoscaler.resources;

namespace poolautoscaler.dimensions
{
    internal class DimensionAzureSqlElasticPoolDtu : IDimension
    {
        /// <summary>
        /// Check that this rule can be applied to the given resource.
        /// </summary>
        /// <param name="resource"></param>
        /// <param name="rule"></param>
        /// <returns></returns>
        public bool CanApplyDimension(ResourceState resource, ScalingRule rule, ILogger logger)
        {
            if (resource.Resource is ElasticPoolResource
                && rule.Dimension == "Dtu")
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
            if (int.TryParse(value, out var parsed))
            {
                if (MssqlElasticPoolResourceStateHelper.StandardDtuCapacities.Contains(parsed)
                    || MssqlElasticPoolResourceStateHelper.PremiumDtuCapacities.Contains(parsed))
                {
                    return;

                }
            }

            throw new ArgumentException("DimensionAzureSqlElasticPoolCapacity value not supported.");
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
            if (int.TryParse(dimensionValue1, out var value1) && int.TryParse(dimensionValue2, out var value2))
            {
                return value1.CompareTo(value2);
            }

            throw new ArgumentException($"Invalid dimension values. Value1: '{dimensionValue1}', Value2: '{dimensionValue2}'");
        }

        public string GetCurrentDimensionValue(ResourceState resource)
        {
            if (!(resource.Resource is ElasticPoolResource elasticPool))
            {
                throw new ArgumentException($"Resource is not {nameof(ElasticPoolResource)}.");
            }

            return elasticPool.Data.Sku.Capacity.ToString()!;
        }

        public string GetNextDimensionValue(ResourceState resource, string value)
        {
            if (!(resource.Resource is ElasticPoolResource elasticPool))
            {
                throw new ArgumentException($"Resource is not {nameof(ElasticPoolResource)}.");
            }

            var capacityValues = MssqlElasticPoolResourceStateHelper.GetCapacityValues(elasticPool.Data.Sku);

            int position = Array.IndexOf(capacityValues, int.Parse(value));

            if (position < capacityValues.Length - 1)
            {
                return capacityValues[position + 1].ToString();
            }

            // Already maxed out
            return value;
        }

        public string? GetRequestedDimensionValue(ResourceState resource)
        {
            if (!(resource is MssqlElasticPoolResourceState elasticPoolState))
            {
                throw new ArgumentException($"Resource is not {nameof(MssqlElasticPoolResourceState)}.");
            }

            return elasticPoolState.RequestedMssqlElasticPoolState?.Sku?.Capacity?.ToString();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="resource"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public string GetPreviousDimensionValue(ResourceState resource, string value)
        {
            var previous = InternalGetPreviousDimensionValue(resource, value);
            return previous;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="resource"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public string InternalGetPreviousDimensionValue(ResourceState resource, string value)
        {
            if (!(resource.Resource is ElasticPoolResource elasticPool))
            {
                throw new ArgumentException($"Resource is not {nameof(ElasticPoolResource)}.");
            }

            var capacityValues = MssqlElasticPoolResourceStateHelper.GetCapacityValues(elasticPool.Data.Sku);

            var position = Array.IndexOf(capacityValues, int.Parse(value));

            if (position == 0)
            {
                // If current one is the lowest, then this is the target.
                return value;
            }
            position--;
            return capacityValues[position].ToString();
        }

        public async Task SetDimensionValue(
            CancellationToken stoppingToken,
            ResourceState resource,
            ILogger logger,
            TokenCredential credential,
            string value)
        {
            if (!(resource.Resource is ElasticPoolResource elasticPool))
            {
                throw new ArgumentException($"Resource is not {nameof(ElasticPoolResource)}.");
            }

            ValidateDimensionValue(value);

            (resource as MssqlElasticPoolResourceState).SetDtuCapacity(int.Parse(value));
        }
    }
}
