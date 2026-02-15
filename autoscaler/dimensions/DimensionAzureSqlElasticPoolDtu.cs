using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.Sql;
using Microsoft.Extensions.Logging;
using poolautoscaler.configuration;
using poolautoscaler.resourcemanagement;
using poolautoscaler.resources.MssqlElasticPool;

namespace poolautoscaler.dimensions
{
    /// <summary>SQL Elastic Pool DTU dimension.</summary>
    internal class DimensionAzureSqlElasticPoolDtu : IDimension
    {
        /// <summary>Returns true if resource is elastic pool and rule dimension is Dtu.</summary>
        /// <param name="resource">The resource state.</param>
        /// <param name="rule">The scaling rule.</param>
        /// <param name="logger">The logger.</param>
        /// <returns>True if the dimension can be applied.</returns>
        /// <inheritdoc/>
        public bool CanApplyDimension(ResourceState resource, ScalingRule rule, ILogger logger)
        {
            if (resource.Resource is ElasticPoolResource
                && rule.Dimension == "Dtu")
            {
                return true;
            }

            return false;
        }

        /// <summary>Validates rule dimension values.</summary>
        /// <param name="rule">The scaling rule.</param>
        /// <inheritdoc/>
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
        /// <inheritdoc/>
        public int Compare(ArmResource resource, string dimensionValue1, string dimensionValue2)
        {
            if (int.TryParse(dimensionValue1, out var value1) && int.TryParse(dimensionValue2, out var value2))
            {
                return value1.CompareTo(value2);
            }

            throw new ArgumentException($"Invalid dimension values. Value1: '{dimensionValue1}', Value2: '{dimensionValue2}'");
        }

        /// <summary>Returns current DTU capacity.</summary>
        /// <param name="resource">The resource state.</param>
        /// <returns>Current DTU capacity as string.</returns>
        /// <inheritdoc/>
        public string GetCurrentDimensionValue(ResourceState resource)
        {
            if (!(resource.Resource is ElasticPoolResource elasticPool))
            {
                throw new ArgumentException($"Resource is not {nameof(ElasticPoolResource)}.");
            }

            return elasticPool.Data.Sku.Capacity.ToString()!;
        }

        /// <inheritdoc/>
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

        /// <inheritdoc/>
        public string? GetRequestedDimensionValue(ResourceState resource)
        {
            if (!(resource is MssqlElasticPoolResourceState elasticPoolState))
            {
                throw new ArgumentException($"Resource is not {nameof(MssqlElasticPoolResourceState)}.");
            }

            return elasticPoolState.RequestedMssqlElasticPoolState?.Sku?.Capacity?.ToString();
        }

        /// <summary>Gets the previous dimension value.</summary>
        /// <param name="resource">The resource state.</param>
        /// <param name="value">The current dimension value.</param>
        /// <returns>The previous dimension value.</returns>
        /// <inheritdoc/>
        public string GetPreviousDimensionValue(ResourceState resource, string value)
        {
            var previous = this.InternalGetPreviousDimensionValue(resource, value);
            return previous;
        }

        /// <summary>Gets the previous dimension value internally.</summary>
        /// <param name="resource">The resource state.</param>
        /// <param name="value">The current dimension value.</param>
        /// <returns>The previous dimension value.</returns>
        /// <inheritdoc/>
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

        /// <inheritdoc/>
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

            (resource as MssqlElasticPoolResourceState).SetDtuCapacity(int.Parse(value));
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
    }
}
