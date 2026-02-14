using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.Sql;
using Microsoft.Extensions.Logging;
using poolautoscaler.configuration;
using poolautoscaler.resourcemanagement;
using poolautoscaler.resources.MsSqlDatabase;

namespace poolautoscaler.dimensions
{
    /// <summary>
    /// Example properties of the SqlSku object (sqlDatabaseResource.Data.Sku) based on UI Name:
    ///
    /// UI NAME: "DTU: STANDARD"
    ///   Family: null
    ///   Name: "STANDARD"
    ///   Size: null
    ///   Tier: "STANDARD"
    ///
    /// UI NAME: "DTU: PREMIUM"
    ///   Family: null
    ///   Name: "PREMIUM"
    ///   Size: null
    ///   Tier: "PREMIUM"
    ///
    /// UI NAME: "DTU: BASIC"
    ///   Family: null
    ///   Name: "BASIC"
    ///   Size: null
    ///   Tier: "BASIC"
    ///
    /// UI NAME: "Elastic Pool"
    ///   Family: null
    ///   Name: "ElasticPool"
    ///   Size: null
    ///   Tier: (varies based on the pool's tier)
    ///
    /// UI NAME: "VCORE: General Purpose"
    ///   Family: "Gen_5"
    ///   Name: "GP_Gen_5"
    ///   Size: null
    ///   Tier: "GeneralPurpose".
    /// </summary>
    internal class DimensionAzureSqlDatabaseDtu : IDimension
    {
        /// <summary>
        /// Check that this rule can be applied to the given resource.
        /// </summary>
        /// <param name="resource">The resource state.</param>
        /// <param name="rule">The scaling rule.</param>
        /// <param name="logger">The logger.</param>
        /// <returns>True if the dimension can be applied.</returns>
        public bool CanApplyDimension(ResourceState resource, ScalingRule rule, ILogger logger)
        {
            if (resource.Resource is SqlDatabaseResource sqlDatabaseResource
                && rule.Dimension == "Dtu")
            {
                if (sqlDatabaseResource.Data.Sku.Name == "ElasticPool" || sqlDatabaseResource.Data.Sku.Family != null)
                {
                    logger.LogDebug($"SqlDatabase belongs to an elastic pool ('{sqlDatabaseResource.Data.ElasticPoolId}') or a non DTU service family ({sqlDatabaseResource.Data.Sku.Family}) and can not have a DTU dimension applied directly. Rule '{rule.Id}' will not be applied.");
                    return false;
                }

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

        /// <summary>
        /// Compare two dimension values.
        /// </summary>
        /// <param name="resource">The ARM resource (unused).</param>
        /// <param name="dimensionValue1">First dimension value.</param>
        /// <param name="dimensionValue2">Second dimension value.</param>
        /// <returns>Comparison result.</returns>
        /// <exception cref="ArgumentException">Thrown when values are not valid integers.</exception>
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
            if (!(resource.Resource is SqlDatabaseResource sqlDatabase))
            {
                throw new ArgumentException($"Resource is not {nameof(SqlDatabaseResource)}.");
            }

            return sqlDatabase.Data.Sku.Capacity.ToString()!;
        }

        public string GetNextDimensionValue(ResourceState resource, string value)
        {
            if (!(resource.Resource is SqlDatabaseResource sqlDatabase))
            {
                throw new ArgumentException($"Resource is not {nameof(SqlDatabaseResource)}.");
            }

            var capacityValues = MsSqlDatabaseResourceStateHelper.GetCapacityValues(sqlDatabase.Data.Sku);

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
            if (!(resource is MsSqlDatabaseResourceState sqlDatabaseState))
            {
                throw new ArgumentException($"Resource is not {nameof(MsSqlDatabaseResourceState)}.");
            }

            return sqlDatabaseState.RequestedMsSqlDatabaseState?.Sku?.Capacity?.ToString();
        }

        public string GetPreviousDimensionValue(ResourceState resource, string value)
        {
            if (!(resource.Resource is SqlDatabaseResource sqlDatabase))
            {
                throw new ArgumentException($"Resource is not {nameof(SqlDatabaseResource)}.");
            }

            var capacityValues = MsSqlDatabaseResourceStateHelper.GetCapacityValues(sqlDatabase.Data.Sku);

            int position = Array.IndexOf(capacityValues, int.Parse(value));

            if (position == 0)
            {
                // If current one is the lowest, then this is the target.
                return value;
            }

            return capacityValues[position - 1].ToString();
        }

        public async Task SetDimensionValue(
            CancellationToken stoppingToken,
            ResourceState resource,
            ILogger logger,
            TokenCredential credential,
            string value)
        {
            if (!(resource.Resource is SqlDatabaseResource elasticPool))
            {
                throw new ArgumentException($"Resource is not {nameof(SqlDatabaseResource)}.");
            }

            this.ValidateDimensionValue(value);

            (resource as MsSqlDatabaseResourceState).SetDtuCapacity(int.Parse(value));
        }

        private void ValidateDimensionValue(string value)
        {
            if (int.TryParse(value, out var parsed))
            {
                if (MsSqlDatabaseResourceStateHelper.StandardDtuCapacities.Contains(parsed)
                    || MsSqlDatabaseResourceStateHelper.PremiumDtuCapacities.Contains(parsed))
                {
                    return;

                }
            }

            throw new ArgumentException("DimensionAzureSqlElasticPoolCapacity value not supported.");
        }
    }
}
