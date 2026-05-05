using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.Sql;
using Microsoft.Extensions.Logging;
using poolautoscaler.configuration;
using poolautoscaler.resourcemanagement;
using poolautoscaler.resources.MssqlElasticPool;
using poolautoscaler.utils;

namespace poolautoscaler.dimensions
{
    /// <summary>SQL Elastic Pool per-database max eDTU dimension.</summary>
    internal class DimensionAzureSqlElasticPoolPerDatabaseMaxCapacity : IDimension
    {
        /// <inheritdoc/>
        public bool CanApplyDimension(ResourceState resource, ScalingRule rule, ILogger logger)
        {
            if (resource.Resource is ElasticPoolResource
                && rule.Dimension == "PerDatabaseMaxCapacity")
            {
                return true;
            }

            return false;
        }

        /// <inheritdoc/>
        public void ValidateRuleConfiguration(ScalingRule rule)
        {
        }

        /// <inheritdoc/>
        public int Compare(ArmResource resource, string dimensionValue1, string dimensionValue2)
        {
            if (IntParseUtils.TryParseInt(dimensionValue1, out var value1) && IntParseUtils.TryParseInt(dimensionValue2, out var value2))
            {
                return value1.CompareTo(value2);
            }

            throw new ArgumentException($"Invalid dimension values. Value1: '{dimensionValue1}', Value2: '{dimensionValue2}'");
        }

        /// <inheritdoc/>
        public string GetCurrentDimensionValue(ResourceState resource)
        {
            if (!(resource.Resource is ElasticPoolResource elasticPool))
            {
                throw new ArgumentException($"Resource is not {nameof(ElasticPoolResource)}.");
            }

            return elasticPool.Data.PerDatabaseSettings?.MaxCapacity?.ToString() ?? string.Empty;
        }

        /// <inheritdoc/>
        public string GetNextDimensionValue(ResourceState resource, string value)
        {
            if (!(resource.Resource is ElasticPoolResource elasticPool))
            {
                throw new ArgumentException($"Resource is not {nameof(ElasticPoolResource)}.");
            }

            var poolDtu = (int)(elasticPool.Data.Sku.Capacity ?? throw new InvalidOperationException("Elastic pool SKU capacity is not set."));
            var capacityValues = MssqlElasticPoolResourceStateHelper.GetPerDbMaxCapacityValues(elasticPool.Data.Sku, poolDtu);

            int position = Array.IndexOf(capacityValues, IntParseUtils.ParseInt(value));

            if (position < capacityValues.Length - 1)
            {
                return capacityValues[position + 1].ToString();
            }

            return value;
        }

        /// <inheritdoc/>
        public string? GetRequestedDimensionValue(ResourceState resource)
        {
            if (!(resource is MssqlElasticPoolResourceState elasticPoolState))
            {
                throw new ArgumentException($"Resource is not {nameof(MssqlElasticPoolResourceState)}.");
            }

            return elasticPoolState.RequestedMssqlElasticPoolState?.PerDatabaseMaxCapacity?.ToString();
        }

        /// <summary>Gets the previous dimension value.</summary>
        /// <param name="resource">The resource state.</param>
        /// <param name="value">The current dimension value.</param>
        /// <returns>The previous dimension value.</returns>
        /// <inheritdoc/>
        public string GetPreviousDimensionValue(ResourceState resource, string value)
        {
            return this.InternalGetPreviousDimensionValue(resource, value);
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

            var poolDtu = (int)(elasticPool.Data.Sku.Capacity ?? throw new InvalidOperationException("Elastic pool SKU capacity is not set."));
            var capacityValues = MssqlElasticPoolResourceStateHelper.GetPerDbMaxCapacityValues(elasticPool.Data.Sku, poolDtu);

            var position = Array.IndexOf(capacityValues, IntParseUtils.ParseInt(value));

            if (position <= 0)
            {
                return value;
            }

            position--;
            return capacityValues[position].ToString();
        }

        /// <inheritdoc/>
        public Task SetDimensionValue(
            CancellationToken stoppingToken,
            ResourceState resource,
            ILogger logger,
            TokenCredential credential,
            string value)
        {
            if (resource.Resource is not ElasticPoolResource)
            {
                throw new ArgumentException($"Resource is not {nameof(ElasticPoolResource)}.");
            }

            if (resource is not MssqlElasticPoolResourceState poolState)
            {
                throw new ArgumentException($"Resource is not {nameof(MssqlElasticPoolResourceState)}.");
            }

            var parsed = IntParseUtils.ParseInt(value);
            poolState.SetPerDatabaseMaxCapacity(parsed);
            return Task.CompletedTask;
        }
    }
}
