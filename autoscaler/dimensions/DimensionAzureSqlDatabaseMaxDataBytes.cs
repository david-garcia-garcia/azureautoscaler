using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.Sql;
using Microsoft.Extensions.Logging;
using poolautoscaler.resources;

namespace poolautoscaler.dimensions
{
    internal class DimensionAzureSqlDatabaseMaxDataBytes : IDimension
    {
        /// <summary>
        /// Check that this rule can be applied to the given resource.
        /// </summary>
        /// <param name="resource"></param>
        /// <param name="rule"></param>
        /// <param name="logger"></param>
        /// <returns></returns>
        public bool CanApplyDimension(ResourceState resource, ScalingRule rule, ILogger logger)
        {
            if (resource.Resource is SqlDatabaseResource sqlDatabaseResource
                && rule.Dimension == "MaxDataBytes")
            {
                // Check if Hyperscale or Business Critical Serverless (not supported)
                try
                {
                    var sku = sqlDatabaseResource.Data.Sku;
                    var validSizes = MsSqlDatabaseResourceStateHelper.GetValidStorageSizesForDatabase(sku);
                    return true;
                }
                catch (NotSupportedException)
                {
                    logger.LogWarning($"MaxDataBytes dimension is not supported for this database SKU configuration. Rule '{rule.Id}' will not be applied.");
                    return false;
                }
            }

            return false;
        }

        public void ValidateRuleConfiguration(ScalingRule rule)
        {
            // Validation can be added here if needed
        }

        /// <summary>
        /// Compare two dimension values (storage in bytes)
        /// </summary>
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
            if (!(resource.Resource is SqlDatabaseResource sqlDatabase))
            {
                throw new ArgumentException($"Resource is not {nameof(SqlDatabaseResource)}.");
            }

            return sqlDatabase.Data.MaxSizeBytes.ToString();
        }

        public string GetNextDimensionValue(ResourceState resource, string value)
        {
            if (!(resource.Resource is SqlDatabaseResource sqlDatabase))
            {
                throw new ArgumentException($"Resource is not {nameof(SqlDatabaseResource)}.");
            }

            var currentBytes = long.Parse(value);
            var sku = sqlDatabase.Data.Sku;
            var nextBytes = MsSqlDatabaseResourceStateHelper.GetNextValidStorageSizeForDatabase(currentBytes, sku);

            return nextBytes.ToString();
        }

        public string? GetRequestedDimensionValue(ResourceState resource)
        {
            if (!(resource is MsSqlDatabaseResourceState sqlDatabaseState))
            {
                throw new ArgumentException($"Resource is not {nameof(MsSqlDatabaseResourceState)}.");
            }

            return sqlDatabaseState.RequestedMsSqlDatabaseState?.MaxSizeBytes?.ToString();
        }

        public string GetPreviousDimensionValue(ResourceState resource, string value)
        {
            if (!(resource.Resource is SqlDatabaseResource sqlDatabase))
            {
                throw new ArgumentException($"Resource is not {nameof(SqlDatabaseResource)}.");
            }

            var currentBytes = long.Parse(value);
            var sku = sqlDatabase.Data.Sku;
            var previousBytes = MsSqlDatabaseResourceStateHelper.GetPreviousValidStorageSizeForDatabase(currentBytes, sku);

            return previousBytes.ToString();
        }

        public async Task SetDimensionValue(
            CancellationToken stoppingToken,
            ResourceState resource,
            ILogger logger,
            TokenCredential credential,
            string value)
        {
            if (!(resource.Resource is SqlDatabaseResource sqlDatabase))
            {
                throw new ArgumentException($"Resource is not {nameof(SqlDatabaseResource)}.");
            }

            var sku = sqlDatabase.Data.Sku;
            var floatValue = float.Parse(value);
            var requestedBytes = (long)floatValue;

            // Validate and normalize to closest valid size
            var normalizedBytes = MsSqlDatabaseResourceStateHelper.FindClosestValidStorageSizeForDatabase(requestedBytes, sku);

            (resource as MsSqlDatabaseResourceState).SetMaxSizeBytes(normalizedBytes);
        }
    }
}

