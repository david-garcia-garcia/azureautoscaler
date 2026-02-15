using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.Sql;
using Microsoft.Extensions.Logging;
using poolautoscaler.configuration;
using poolautoscaler.resourcemanagement;
using poolautoscaler.resources.MssqlElasticPool;

namespace poolautoscaler.dimensions
{
    /// <summary>SQL Elastic Pool max data size (bytes) dimension.</summary>
    internal class DimensionAzureSqlElasticPoolMaxDataBytes : IDimension
    {
        /// <summary>Check that this rule can be applied to the given resource.</summary>
        /// <param name="resource">The resource state.</param>
        /// <param name="rule">The scaling rule.</param>
        /// <param name="logger">The logger.</param>
        /// <returns>True if the dimension can be applied.</returns>
        public bool CanApplyDimension(ResourceState resource, ScalingRule rule, ILogger logger)
        {
            if (resource.Resource is ElasticPoolResource
                && rule.Dimension == "MaxDataBytes")
            {
                return true;
            }

            return false;
        }

        public void ValidateRuleConfiguration(ScalingRule rule)
        {
            // this.ValidateDimensionValue(rule.DimensionValueMin);
            // this.ValidateDimensionValue(rule.DimensionValueMax);
            // this.ValidateDimensionValue(rule.DimensionValue);
        }

        /// <summary>Compare two dimension values.</summary>
        /// <param name="resource">The ARM resource (unused).</param>
        /// <param name="dimensionValue1">First dimension value.</param>
        /// <param name="dimensionValue2">Second dimension value.</param>
        /// <returns>Comparison result.</returns>
        /// <exception cref="ArgumentException">Thrown when values are invalid.</exception>
        public int Compare(ArmResource resource, string dimensionValue1, string dimensionValue2)
        {
            if (double.TryParse(dimensionValue1, out var value1) && double.TryParse(dimensionValue2, out var value2))
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

            return elasticPool.Data.MaxSizeBytes.ToString();
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

        /// <summary>Gets the previous dimension value.</summary>
        /// <param name="resource">The resource state.</param>
        /// <param name="value">The current dimension value.</param>
        /// <returns>The previous dimension value.</returns>
        public string GetPreviousDimensionValue(ResourceState resource, string value)
        {
            var previous = this.InternalGetPreviousDimensionValue(resource, value);
            return previous;
        }

        public string? GetRequestedDimensionValue(ResourceState resource)
        {
            if (!(resource is MssqlElasticPoolResourceState elasticPoolState))
            {
                throw new ArgumentException($"Resource is not {nameof(MssqlElasticPoolResourceState)}.");
            }

            return elasticPoolState.RequestedMssqlElasticPoolState?.MaxSizeBytes?.ToString();
        }

        /// <summary>Gets the previous dimension value internally.</summary>
        /// <param name="resource">The resource state.</param>
        /// <param name="value">The current dimension value.</param>
        /// <returns>The previous dimension value.</returns>
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

            this.ValidateDimensionValue(value);

            (resource as MssqlElasticPoolResourceState).SetMaxSizeBytes((long)double.Parse(value));
        }

        private void ValidateDimensionValue(string value)
        {
        }
    }
}
