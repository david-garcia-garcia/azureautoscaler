using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.PostgreSql.FlexibleServers;
using Microsoft.Extensions.Logging;
using poolautoscaler.configuration;
using poolautoscaler.resourcemanagement;
using poolautoscaler.resources.PostgreSqlFlexibleServer;

namespace poolautoscaler.dimensions
{
    /// <summary>PostgreSQL Flexible Server IOPS dimension.</summary>
    internal class DimensionPostgreSqlFlexibleServerIops : IDimension
    {
        /// <summary>Initializes a new instance of the <see cref="DimensionPostgreSqlFlexibleServerIops"/> class.</summary>
        public DimensionPostgreSqlFlexibleServerIops()
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
                && rule.Dimension == "Iops")
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
        /// <param name="resource">The ARM resource (unused).</param>
        /// <param name="dimensionValue1">First dimension value.</param>
        /// <param name="dimensionValue2">Second dimension value.</param>
        /// <returns>Comparison result.</returns>
        /// <exception cref="ArgumentException">Thrown when values are invalid.</exception>
        /// <inheritdoc/>
        public int Compare(ArmResource resource, string dimensionValue1, string dimensionValue2)
        {
            var value1 = float.Parse(dimensionValue1);
            var value2 = float.Parse(dimensionValue2);
            return value1.CompareTo(value2);
        }

        /// <inheritdoc/>
        public string GetCurrentDimensionValue(ResourceState resource)
        {
            if (!(resource.Resource is PostgreSqlFlexibleServerResource sqlDatabase))
            {
                throw new ArgumentException("Resource is not a PostgreSqlFlexibleServerResource.");
            }

            return (sqlDatabase.Data.Storage?.Iops ?? 0).ToString();
        }

        /// <inheritdoc/>
        public string GetNextDimensionValue(ResourceState resource, string value)
        {
            var current = int.Parse(value);
            return (current + 1).ToString();
        }

        /// <inheritdoc/>
        public string GetPreviousDimensionValue(ResourceState resource, string value)
        {
            var current = int.Parse(value);
            var result = current - 1;

            if (result < 1)
            {
                result = 1;
            }

            return result.ToString();
        }

        /// <inheritdoc/>
        public string? GetRequestedDimensionValue(ResourceState resource)
        {
            if (!(resource is PostgreSqlFlexibleServerResourceState postgresState))
            {
                throw new ArgumentException($"Resource is not {nameof(PostgreSqlFlexibleServerResourceState)}.");
            }

            return postgresState.RequestedPostgreSqlFlexibleServerState?.Iops?.ToString();
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

            (resource as PostgreSqlFlexibleServerResourceState).SetIops(value);
        }

        private void ValidateDimensionValue(string value)
        {
            if (!float.TryParse(value, out _))
            {
                throw new Exception("Invalid Iops value");
            }
        }
    }
}
