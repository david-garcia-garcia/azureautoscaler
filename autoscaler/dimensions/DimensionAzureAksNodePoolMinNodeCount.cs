using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.ContainerService;
using Microsoft.Extensions.Logging;
using poolautoscaler.resources;

namespace poolautoscaler.dimensions
{
    /***
     * This scaler simply moves the minimum node count according to pool real CPU usage. The MAX node count
     * configured for the pool is never exceeded.
     * 
     * This is because the embeded autoscaler only scales when pods cannot be scheduled, relying solely on cpu requests
     * and not accounting for real CPU usage in the cluster.
     * 
     * The original minimum node size configured for the pool will be lost if this
     * scaler is used. The maximum will be honoured.
     */
    internal class DimensionAzureAksNodePoolMinNodeCount : IDimension
    {
        public DimensionAzureAksNodePoolMinNodeCount()
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
            if (resource.Resource is ContainerServiceAgentPoolResource
                && rule.Dimension == "MinNodeCount")
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
            if (int.TryParse(dimensionValue1, out var value1) && int.TryParse(dimensionValue2, out var value2))
            {
                return value1.CompareTo(value2);
            }

            throw new ArgumentException($"Invalid dimension values. Value1: '{dimensionValue1}', Value2: '{dimensionValue2}'");
        }

        public string GetCurrentDimensionValue(ResourceState resource)
        {
            if (!(resource.Resource is ContainerServiceAgentPoolResource agentPool))
            {
                throw new ArgumentException($"Resource is not {nameof(ContainerServiceAgentPoolResource)}.");
            }

            return agentPool.Data.Count.ToString()!;
        }

        public string GetNextDimensionValue(ResourceState resource, string value)
        {
            if (!(resource.Resource is ContainerServiceAgentPoolResource agentPool))
            {
                throw new ArgumentException($"Resource is not {nameof(ContainerServiceAgentPoolResource)}.");
            }

            return (int.Parse(value) + 1).ToString();
        }

        public string? GetRequestedDimensionValue(ResourceState resource)
        {
            if (!(resource is AksNodePoolResourceState aksNodePoolState))
            {
                throw new ArgumentException($"Resource is not {nameof(AksNodePoolResourceState)}.");
            }

            return aksNodePoolState.RequestedAksNodePoolState?.MinNodeCount?.ToString();
        }

        public string GetPreviousDimensionValue(ResourceState resource, string value)
        {
            return (int.Parse(value) - 1).ToString();
        }

        public async Task SetDimensionValue(
            CancellationToken stoppingToken,
            ResourceState resource,
            ILogger logger,
            TokenCredential credential,
            string value)
        {
            if (!(resource.Resource is ContainerServiceAgentPoolResource elasticPool))
            {
                throw new ArgumentException($"Resource is not {nameof(ContainerServiceAgentPoolResource)}.");
            }

            ValidateDimensionValue(value);

            var requestedMinCount = int.Parse(value);

            (resource as AksNodePoolResourceState).SetMinNodeCount(requestedMinCount);
        }
    }
}
