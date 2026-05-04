using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.ContainerService;
using Microsoft.Extensions.Logging;
using poolautoscaler.configuration;
using poolautoscaler.resourcemanagement;
using poolautoscaler.resourcemanagement.Dto;

namespace poolautoscaler.resources.AksNodePool
{
    /// <summary>
    /// Helper for expanding and resolving AKS node pool resource IDs.
    /// </summary>
    public static class AksNodePoolResourceStateHelper
    {
        /// <summary>
        /// Expands a node pool resource ID that may contain wildcards into concrete resource IDs.
        /// </summary>
        /// <param name="client">The ARM client.</param>
        /// <param name="key">The key for the resource entry.</param>
        /// <param name="resourceId">The resource ID or wildcard pattern.</param>
        /// <param name="logger">The logger.</param>
        /// <param name="stoppingToken">Cancellation token.</param>
        /// <returns>A dictionary of key to expanded resource IDs.</returns>
        public static async Task<Dictionary<string, ExpandedResource>> ExpandNodePoolWildcard(ArmClient client, string key, string resourceId, ILogger logger, CancellationToken stoppingToken)
        {
            var result = new Dictionary<string, ExpandedResource>();

            var match = ResourceStateFactory.AksNodePool.Match(resourceId);
            if (!match.Success)
            {
                throw new ArgumentException("Invalid AKS Node Pool resource ID", nameof(resourceId));
            }

            var nodePoolName = match.Groups["nodePoolName"].Value;
            var pattern = ResourceStateFactory.GetResourcePattern(nodePoolName);
            if (pattern == null)
            {
                result.Add(key, new ExpandedResource { ResourceId = resourceId, Context = null });
                return result;
            }

            logger.LogDebug("Expanding resource wildcard: " + resourceId);

            var clusterId = $"/subscriptions/{match.Groups["subscriptionId"].Value}/resourceGroups/{match.Groups["resourceGroupName"].Value}/providers/Microsoft.ContainerService/managedClusters/{match.Groups["clusterName"].Value}";

            var cluster = await client.GetContainerServiceManagedClusterResource(new ResourceIdentifier(clusterId)).GetAsync(cancellationToken: stoppingToken);

            var nodePools = cluster.Value.GetContainerServiceAgentPools().GetAll(stoppingToken);

            foreach (var nodePool in nodePools)
            {
                if (pattern.IsMatch(nodePool.Data.Name))
                {
                    var expandedId = $"{clusterId}/agentPools/{nodePool.Data.Name}";
                    var context = new ResourceFilterContext
                    {
                        ResourceName = nodePool.Data.Name,
                        Tags = nodePool.Data.Tags ?? new Dictionary<string, string>(),
                        Resource = nodePool,
                    };
                    result.Add(key + "_" + nodePool.Data.Name, new ExpandedResource { ResourceId = expandedId, Context = context });
                    logger.LogDebug("Expanded AKS node pool: {0}", expandedId);
                }
            }

            return result;
        }
    }
}
