using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.MySql.FlexibleServers;
using Azure.ResourceManager.MySql.FlexibleServers.Models;
using Microsoft.Extensions.Logging;
using poolautoscaler.configuration;
using poolautoscaler.resourcemanagement;
using poolautoscaler.resources.MySqlFlexibleServer;

namespace poolautoscaler.dimensions
{
    /// <summary>MySQL Flexible Server IOPS dimension.</summary>
    internal class DimensionMySqlFlexibleServerIops : IDimension
    {
        public DimensionMySqlFlexibleServerIops()
        {
        }

        /// <summary>Check that this rule can be applied to the given resource.</summary>
        /// <param name="resource">The resource state.</param>
        /// <param name="rule">The scaling rule.</param>
        /// <param name="logger">The logger.</param>
        /// <returns>True if the dimension can be applied.</returns>
        public bool CanApplyDimension(ResourceState resource, ScalingRule rule, ILogger logger)
        {
            if (resource.Resource is MySqlFlexibleServerResource
                && rule.Dimension == "Iops")
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

            if (sqlDatabase.Data.Storage.AutoIoScaling == MySqlFlexibleServerEnableStatusEnum.Enabled)
            {
                throw new Exception("Cannot evaluate IOPS in servers with AutoIoScaling enabled.");
            }

            return sqlDatabase.Data.Storage.Iops.ToString();
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

            return mysqlState.RequestedMySqlFlexibleServerState?.Iops?.ToString();
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

            this.ValidateDimensionValue(value);

            (resource as MySqlFlexibleServerResourceState).SetIops(value);
        }

        private void ValidateDimensionValue(string value)
        {
            if (!float.TryParse(value, out _))
            {
                throw new Exception("Invalid Iops value {value}");
            }
        }
    }
}
