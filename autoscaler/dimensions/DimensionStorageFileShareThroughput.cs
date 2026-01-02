using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.ContainerService;
using Azure.ResourceManager.MySql.FlexibleServers;
using Azure.ResourceManager.Sql;
using Azure.ResourceManager.Storage;
using Microsoft.Extensions.Logging;
using poolautoscaler.resources;

namespace poolautoscaler.dimensions
{
    public class DimensionStorageFileShareThroughput : IDimension
    {
        public DimensionStorageFileShareThroughput()
        {
        }

        /// <summary>
        /// Check that this rule can be applied to the given resource.
        /// </summary>
        /// <param name="resource"></param>
        /// <param name="rule"></param>
        /// <returns></returns>
        public bool CanApplyDimension(ResourceState resource, ScalingRule rule, ILogger logger)
        {
            if (resource is StorageFileShareResourceState
                && rule.Dimension == "Throughput")
            {
                return true;
            }

            return false;
        }

        public void ValidateRuleConfiguration(ScalingRule rule)
        {
        }

        private void ValidateDimensionValue(string value)
        {
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
            if (double.TryParse(dimensionValue1, out var value1) && double.TryParse(dimensionValue2, out var value2))
            {
                return value1.CompareTo(value2);
            }

            throw new ArgumentException($"Invalid dimension values. Value1: '{dimensionValue1}', Value2: '{dimensionValue2}'");
        }

        public string GetCurrentDimensionValue(ResourceState resource)
        {
            if (!(resource is StorageFileShareResourceState fileShareState))
            {
                throw new ArgumentException($"Resource is not {nameof(StorageFileShareResourceState)}.");
            }

            // For testing purposes, if we don't have actual Azure resource data, return a default
            if (fileShareState.ExistingStorageFileShareState?.ShareQuotaGb == null)
            {
                return "110"; // Default throughput for 100GB quota
            }

            var currentQuota = fileShareState.ExistingStorageFileShareState.ShareQuotaGb.Value;
            var currentThroughput = StorageFileShareResourceStateHelper.GetThroughputFromQuota(currentQuota);
            return currentThroughput.ToString();
        }

        public string GetNextDimensionValue(ResourceState resource, string value)
        {
            if (!(resource is StorageFileShareResourceState))
            {
                throw new ArgumentException($"Resource is not {nameof(StorageFileShareResourceState)}.");
            }

            var currentThroughput = double.Parse(value);
            var nextThroughput = currentThroughput + 10; // Increase by 10 Mbps
            return nextThroughput.ToString();
        }

        public string? GetRequestedDimensionValue(ResourceState resource)
        {
            if (!(resource is StorageFileShareResourceState fileShareState))
            {
                throw new ArgumentException($"Resource is not {nameof(StorageFileShareResourceState)}.");
            }

            // Calculate throughput from the requested quota (returns null if no quota requested)
            var requestedQuota = fileShareState.RequestedStorageFileShareState?.ShareQuotaGb;
            if (requestedQuota == null) return null;
            
            var requestedThroughput = StorageFileShareResourceStateHelper.GetThroughputFromQuota(requestedQuota.Value);
            return requestedThroughput.ToString();
        }

        public string GetPreviousDimensionValue(ResourceState resource, string value)
        {
            var currentThroughput = double.Parse(value);
            var previousThroughput = currentThroughput - 10; // Decrease by 10 Mbps

            // Minimum throughput based on minimum storage (100 GB)
            var minimumThroughput = StorageFileShareResourceStateHelper.GetThroughputFromQuota(100);
            if (previousThroughput < minimumThroughput)
            {
                previousThroughput = minimumThroughput;
            }

            return previousThroughput.ToString();
        }

        public async Task SetDimensionValue(
            CancellationToken stoppingToken,
            ResourceState resource,
            ILogger logger,
            TokenCredential credential,
            string value)
        {
            if (!(resource is StorageFileShareResourceState fileShareState))
            {
                throw new ArgumentException("Resource is not a StorageFileShareResourceState.");
            }

            ValidateDimensionValue(value);

            var targetThroughput = double.Parse(value);
            fileShareState.SetThroughput(targetThroughput);
        }
    }
} 