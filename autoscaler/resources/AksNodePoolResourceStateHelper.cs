using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.ContainerService;
using Microsoft.Extensions.Logging;

namespace poolautoscaler.resources
{
    public static class AksNodePoolResourceStateHelper
    {
        public static async Task<Dictionary<string, string>> ExpandNodePoolWildcard(ArmClient client, string key, string resourceId, ILogger logger, CancellationToken stoppingToken)
        {
            var result = new Dictionary<string, string>();

            var match = ResourceStateFactory.AksNodePool.Match(resourceId);
            if (!match.Success)
            {
                throw new ArgumentException("Invalid AKS Node Pool resource ID", nameof(resourceId));
            }

            var nodePoolName = match.Groups["nodePoolName"].Value;
            var pattern = ResourceStateFactory.GetResourcePattern(nodePoolName);
            if (pattern == null)
            {
                result.Add(key, resourceId);
                return result;
            }

            logger.LogDebug("Expanding resource wildcard: " + resourceId);

            // Extract cluster ID from the resource ID
            var clusterId = $"/subscriptions/{match.Groups["subscriptionId"].Value}/resourceGroups/{match.Groups["resourceGroupName"].Value}/providers/Microsoft.ContainerService/managedClusters/{match.Groups["clusterName"].Value}";

            // Get the cluster
            var cluster = await client.GetContainerServiceManagedClusterResource(new ResourceIdentifier(clusterId)).GetAsync(cancellationToken: stoppingToken);

            // Get all node pools in the cluster
            var nodePools = cluster.Value.GetContainerServiceAgentPools().GetAll(stoppingToken);

            // Build the list of resource IDs for each node pool
            foreach (var nodePool in nodePools)
            {
                if (pattern.IsMatch(nodePool.Data.Name))
                {
                    var expandedId = $"{clusterId}/agentPools/{nodePool.Data.Name}";
                    result.Add(key + "_" + nodePool.Data.Name, expandedId);
                    logger.LogDebug("Expanded AKS node pool: {0}", expandedId);
                }
            }

            return result;
        }
    }
} 