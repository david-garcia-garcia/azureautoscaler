using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.Storage;
using Microsoft.Extensions.Logging;
using poolautoscaler.configuration;
using poolautoscaler.resourcemanagement;
using poolautoscaler.resources.StorageFileShare;

namespace poolautoscaler.dimensions
{
    /// <summary>Storage file share provisioned size (GB) dimension.</summary>
    internal class DimensionStorageFileShareProvisionedStorage : IDimension
    {
        public DimensionStorageFileShareProvisionedStorage()
        {
        }

        /// <summary>Check that this rule can be applied to the given resource.</summary>
        /// <param name="resource">The resource state.</param>
        /// <param name="rule">The scaling rule.</param>
        /// <param name="logger">The logger.</param>
        /// <returns>True if the dimension can be applied.</returns>
        public bool CanApplyDimension(ResourceState resource, ScalingRule rule, ILogger logger)
        {
            if (resource.Resource is FileShareResource
                && rule.Dimension == "ProvisionedStorage")
            {
                return true;
            }

            return false;
        }

        public void ValidateRuleConfiguration(ScalingRule rule)
        {
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
            if (!(resource.Resource is FileShareResource agentPool))
            {
                throw new ArgumentException($"Resource is not {nameof(FileShareResource)}.");
            }

            return agentPool.Data.ShareQuota?.ToString();
        }

        public string GetNextDimensionValue(ResourceState resource, string value)
        {
            var result = (double.Parse(value) + 100);

            return result.ToString();
        }

        public string? GetRequestedDimensionValue(ResourceState resource)
        {
            if (!(resource is StorageFileShareResourceState fileShareState))
            {
                throw new ArgumentException($"Resource is not {nameof(StorageFileShareResourceState)}.");
            }

            return fileShareState.RequestedStorageFileShareState?.ShareQuotaGb?.ToString();
        }

        public string GetPreviousDimensionValue(ResourceState resource, string value)
        {
            var result = (double.Parse(value) - 100);

            if (result < 100)
            {
                result = 100;
            }

            return result.ToString();
        }

        public async Task SetDimensionValue(
            CancellationToken stoppingToken,
            ResourceState resource,
            ILogger logger,
            TokenCredential credential,
            string value)
        {
            if (!(resource.Resource is FileShareResource elasticPool))
            {
                throw new ArgumentException("Resource is not a FileShareResource.");
            }

            this.ValidateDimensionValue(value);

            (resource as StorageFileShareResourceState).SetProvisionedStorage(double.Parse(value));
        }

        private void ValidateDimensionValue(string value)
        {
        }
    }
}
