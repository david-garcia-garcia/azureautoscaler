using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.ContainerService;
using Microsoft.Extensions.Logging;
using poolautoscaler.configuration;
using poolautoscaler.resourcemanagement;
using poolautoscaler.resources.AksNodePool;

namespace poolautoscaler.dimensions
{
    /// <summary>Scales AKS node pool minimum node count by real CPU usage.</summary>
    internal class DimensionAzureAksNodePoolMinNodeCount : IDimension
    {
        /// <summary>Initializes a new instance of the <see cref="DimensionAzureAksNodePoolMinNodeCount"/> class.</summary>
        public DimensionAzureAksNodePoolMinNodeCount()
        {
        }

        /// <summary>
        /// Check that this rule can be applied to the given resource.
        /// </summary>
        /// <param name="resource">The resource state.</param>
        /// <param name="rule">The scaling rule.</param>
        /// <param name="logger">The logger.</param>
        /// <returns>True if the dimension can be applied.</returns>
        public bool CanApplyDimension(ResourceState resource, ScalingRule rule, ILogger logger)
        {
            if (resource.Resource is ContainerServiceAgentPoolResource
                && rule.Dimension == "MinNodeCount")
            {
                return true;
            }

            return false;
        }

        /// <summary>No rule validation required.</summary>
        /// <param name="rule">The scaling rule.</param>
        public void ValidateRuleConfiguration(ScalingRule rule)
        {
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

        /// <summary>Returns current min node count.</summary>
        /// <param name="resource">The resource state.</param>
        /// <returns>Current min node count as string.</returns>
        public string GetCurrentDimensionValue(ResourceState resource)
        {
            if (!(resource.Resource is ContainerServiceAgentPoolResource agentPool))
            {
                throw new ArgumentException($"Resource is not {nameof(ContainerServiceAgentPoolResource)}.");
            }

            return agentPool.Data.Count.ToString()!;
        }

        /// <summary>Returns next min node count.</summary>
        /// <param name="resource">The resource state.</param>
        /// <param name="value">The current dimension value.</param>
        /// <returns>Next min node count as string.</returns>
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

        /// <summary>Returns previous min node count.</summary>
        /// <param name="resource">The resource state.</param>
        /// <param name="value">The current dimension value.</param>
        /// <returns>Previous min node count as string.</returns>
        public string GetPreviousDimensionValue(ResourceState resource, string value)
        {
            return (int.Parse(value) - 1).ToString();
        }

        /// <summary>Sets min node count on the pool.</summary>
        /// <param name="stoppingToken">Cancellation token.</param>
        /// <param name="resource">The resource state.</param>
        /// <param name="logger">The logger.</param>
        /// <param name="credential">The token credential.</param>
        /// <param name="value">The dimension value to set.</param>
        /// <returns>A task that completes when the value is set.</returns>
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

            this.ValidateDimensionValue(value);

            var requestedMinCount = int.Parse(value);

            (resource as AksNodePoolResourceState).SetMinNodeCount(requestedMinCount);
        }

        private void ValidateDimensionValue(string value)
        {
        }
    }
}
