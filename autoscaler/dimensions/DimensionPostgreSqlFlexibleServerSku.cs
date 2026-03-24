using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.PostgreSql.FlexibleServers;
using Microsoft.Extensions.Logging;
using poolautoscaler.configuration;
using poolautoscaler.resourcemanagement;
using poolautoscaler.resources.PostgreSqlFlexibleServer;

namespace poolautoscaler.dimensions
{
    /// <summary>PostgreSQL Flexible Server SKU/tier dimension.</summary>
    internal class DimensionPostgreSqlFlexibleServerSku : IDimension
    {
        /// <summary>Initializes a new instance of the <see cref="DimensionPostgreSqlFlexibleServerSku"/> class.</summary>
        public DimensionPostgreSqlFlexibleServerSku()
        {
        }

        /// <summary>Check that this rule can be applied to the given resource.</summary>
        /// <param name="resource">The resource state.</param>
        /// <param name="rule">The scaling rule.</param>
        /// <param name="logger">The logger.</param>
        /// <returns>True if the dimension can be applied.</returns>
        /// <inheritdoc/>
        public bool CanApplyDimension(ResourceState resource, ScalingRule rule, ILogger logger)
        {
            if (resource.Resource is PostgreSqlFlexibleServerResource
                && rule.Dimension == "Sku")
            {
                return true;
            }

            return false;
        }

        /// <inheritdoc/>
        public void ValidateRuleConfiguration(ScalingRule rule)
        {
        }

        /// <summary>Compare two dimension values.</summary>
        /// <param name="resource">The ARM resource.</param>
        /// <param name="dimensionValue1">First dimension value.</param>
        /// <param name="dimensionValue2">Second dimension value.</param>
        /// <returns>Comparison result.</returns>
        /// <exception cref="ArgumentException">Thrown when values are invalid.</exception>
        /// <inheritdoc/>
        public int Compare(ArmResource resource, string dimensionValue1, string dimensionValue2)
        {
            return PostgreSqlFlexibleServerResourceStateHelper.CompareSku((resource as PostgreSqlFlexibleServerResource).Data.Sku.Tier.ToString(), dimensionValue1, dimensionValue2);
        }

        /// <inheritdoc/>
        public string GetCurrentDimensionValue(ResourceState resource)
        {
            if (!(resource.Resource is PostgreSqlFlexibleServerResource sqlDatabase))
            {
                throw new ArgumentException("Resource is not a PostgreSqlFlexibleServerResource.");
            }

            return sqlDatabase.Data.Sku.Name.ToString()!;
        }

        /// <inheritdoc/>
        public string GetNextDimensionValue(ResourceState resource, string value)
        {
            var capacityValues = PostgreSqlFlexibleServerResourceStateHelper.GetCapacityValues(resource.Resource);

            int position = Array.IndexOf(capacityValues, value);

            if (position < capacityValues.Length - 1)
            {
                return capacityValues[position + 1].ToString();
            }

            // Already maxed out
            return value;
        }

        /// <inheritdoc/>
        public string GetPreviousDimensionValue(ResourceState resource, string value)
        {
            var capacityValues = PostgreSqlFlexibleServerResourceStateHelper.GetCapacityValues(resource.Resource);

            int position = Array.IndexOf(capacityValues, value);

            if (position == 0)
            {
                // If current one is the lowest, then this is the target.
                return value;
            }

            return capacityValues[position - 1].ToString();
        }

        /// <inheritdoc/>
        public string? GetRequestedDimensionValue(ResourceState resource)
        {
            if (!(resource is PostgreSqlFlexibleServerResourceState postgresState))
            {
                throw new ArgumentException($"Resource is not {nameof(PostgreSqlFlexibleServerResourceState)}.");
            }

            return postgresState.RequestedPostgreSqlFlexibleServerState?.Sku?.Name;
        }

        /// <inheritdoc/>
        public async Task SetDimensionValue(
            CancellationToken stoppingToken,
            ResourceState resource,
            ILogger logger,
            TokenCredential credential,
            string value)
        {
            if (!(resource.Resource is PostgreSqlFlexibleServerResource elasticPool))
            {
                throw new ArgumentException("Resource is not a PostgreSqlFlexibleServerResource.");
            }

            this.ValidateDimensionValue(value);

            (resource as PostgreSqlFlexibleServerResourceState).SetSku(value);
        }

        private void ValidateDimensionValue(string value)
        {
            if (PostgreSqlFlexibleServerResourceStateHelper.AllSkus.Any(s => s.Sku == value))
            {
                return;
            }

            throw new ArgumentException(
                $"PostgreSQL Flexible Server SKU value '{value}' is not supported. It must match a known flexible server SKU name.");
        }
    }
}
