using System.Text.RegularExpressions;
using Azure.ResourceManager;
using Microsoft.Extensions.Logging;
using poolautoscaler.configuration;
using poolautoscaler.metrics;
using poolautoscaler.resources.AksNodePool;
using poolautoscaler.resources.AzureDevops;
using poolautoscaler.resources.FabricCapacity;
using poolautoscaler.resources.MsSqlDatabase;
using poolautoscaler.resources.MssqlElasticPool;
using poolautoscaler.resources.MySqlFlexibleServer;
using poolautoscaler.resources.StorageFileShare;

namespace poolautoscaler.resourcemanagement
{
    /// <summary>
    /// Factory for creating and expanding resource states by resource ID pattern.
    /// Created states receive the service provider so they can resolve dependencies on demand.
    /// </summary>
    public sealed class ResourceStateFactory : IResourceStateFactory
    {
        private readonly IResourceLocationResolver? resourceLocationResolver;
        private readonly IVmSizeResolver? vmSizeResolver;

        /// <summary>Initializes a new instance of the <see cref="ResourceStateFactory"/> class.</summary>
        /// <param name="serviceProvider">The service provider (reserved for future use).</param>
        /// <param name="resourceLocationResolver">Optional resource location resolver to inject into created states.</param>
        /// <param name="vmSizeResolver">Optional VM size resolver to inject into created states.</param>
        public ResourceStateFactory(
            IServiceProvider serviceProvider,
            IResourceLocationResolver? resourceLocationResolver = null,
            IVmSizeResolver? vmSizeResolver = null)
        {
            this.resourceLocationResolver = resourceLocationResolver;
            this.vmSizeResolver = vmSizeResolver;
        }

        /// <summary>
        /// Regex matching SQL elastic pool resource IDs.
        /// </summary>
        public static readonly Regex ElasticPools = new Regex(@"^/subscriptions/(?<subscriptionId>[^/]+)/resourceGroups/(?<resourceGroupName>[^/]+)/providers/Microsoft.Sql/servers/(?<serverName>[^/]+)/elasticPools/(?<elasticPoolName>[^/]+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Regex matching SQL database resource IDs.
        /// </summary>
        public static readonly Regex SqlDatabase = new Regex(@"^/subscriptions/(?<subscriptionId>[^/]+)/resourceGroups/(?<resourceGroupName>[^/]+)/providers/Microsoft.Sql/servers/(?<serverName>[^/]+)/databases/(?<databaseName>[^/]+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Regex matching MySQL flexible server resource IDs.
        /// </summary>
        public static readonly Regex MySqlFlexibleServer = new Regex(@"^/subscriptions/(?<subscriptionId>[^/]+)/resourceGroups/(?<resourceGroupName>[^/]+)/providers/Microsoft.DBforMySQL/flexibleServers/(?<serverName>[^/]+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Regex matching AKS node pool resource IDs.
        /// </summary>
        public static readonly Regex AksNodePool = new Regex(@"^/subscriptions/(?<subscriptionId>[^/]+)/resourceGroups/(?<resourceGroupName>[^/]+)/providers/Microsoft.ContainerService/managedClusters/(?<clusterName>[^/]+)/agentPools/(?<nodePoolName>[^/]+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Regex matching storage file share resource IDs.
        /// </summary>
        public static readonly Regex FileShare = new Regex(@"^/subscriptions/(?<subscriptionId>[^/]+)/resourceGroups/(?<resourceGroupName>[^/]+)/providers/Microsoft.Storage/storageAccounts/(?<storageAccountName>[^/]+)/fileServices/default/shares/(?<fileShareName>[^/]+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Regex matching Fabric capacity resource IDs.
        /// </summary>
        public static readonly Regex FabricCapacity = new Regex(@"^/subscriptions/(?<subscriptionId>[^/]+)/resourceGroups/(?<resourceGroupName>[^/]+)/providers/Microsoft.Fabric/capacities/(?<capacityName>[^/]+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Regex matching Azure DevOps parallel jobs URIs (azuredevops://organization).
        /// </summary>
        public static readonly Regex AzureDevOpsParallelJobs = new Regex(@"^azuredevops://(?<organization>[^/]+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex RegexPattern = new Regex(@"\{(.*?)\}", RegexOptions.Compiled);

        /// <summary>
        /// Converts a wildcard pattern (e.g. "*" or "{regex}") into a Regex.
        /// </summary>
        /// <param name="pattern">The pattern string.</param>
        /// <returns>A compiled Regex, or null if the pattern is not a valid regex.</returns>
        public static Regex GetResourcePattern(string pattern)
        {
            if (pattern == "*")
            {
                return new Regex(".*", RegexOptions.Compiled);
            }

            var match = RegexPattern.Match(pattern);
            if (match.Success)
            {
                return new Regex(match.Groups[1].Value, RegexOptions.Compiled);
            }

            return null;
        }

        /// <inheritdoc />
        public async Task<Dictionary<string, string>> ExpandResourcesAsync(ArmClient client, string key, string resourceId, ILogger logger, CancellationToken cancellationToken = default)
        {
            // Make sure these are ordered from most specific to least specific
            Match match;

            if ((match = ElasticPools.Match(resourceId)).Success)
            {
                return await MssqlElasticPoolResourceStateHelper.ExpandElasticPoolWildcard(client, key, resourceId, logger, cancellationToken);
            }
            else if ((match = SqlDatabase.Match(resourceId)).Success)
            {
                return await MsSqlDatabaseResourceStateHelper.ExpandSqlDatabaseWildcard(client, key, resourceId, logger, cancellationToken);
            }
            else if ((match = AksNodePool.Match(resourceId)).Success)
            {
                return await AksNodePoolResourceStateHelper.ExpandNodePoolWildcard(client, key, resourceId, logger, cancellationToken);
            }
            else if ((match = FileShare.Match(resourceId)).Success)
            {
                return await StorageFileShareResourceStateHelper.ExpandFileShareWildcard(client, key, resourceId, logger, cancellationToken);
            }

            // Fabric capacities don't support expansion (no wildcards)

            var result = new Dictionary<string, string>();
            result.Add(key, resourceId);
            return result;
        }

        /// <inheritdoc />
        public ResourceState Create(string resourceId, ILogger logger, Resource resourceConfiguration, ResourceInstance? resourceInstance = null)
        {
            // Make sure these are ordered from most specific to least specific
            Match match;
            ResourceState state;

            if ((match = ElasticPools.Match(resourceId)).Success)
            {
                state = new MssqlElasticPoolResourceState(resourceId, logger, resourceConfiguration, this.resourceLocationResolver, this.vmSizeResolver);
                PopulateResourceParts(state, match);
                return state;
            }
            else if ((match = SqlDatabase.Match(resourceId)).Success)
            {
                state = new MsSqlDatabaseResourceState(resourceId, logger, resourceConfiguration, this.resourceLocationResolver, this.vmSizeResolver);
                PopulateResourceParts(state, match);
                return state;
            }
            else if ((match = MySqlFlexibleServer.Match(resourceId)).Success)
            {
                state = new MySqlFlexibleServerResourceState(resourceId, logger, resourceConfiguration, this.resourceLocationResolver, this.vmSizeResolver);
                PopulateResourceParts(state, match);
                return state;
            }
            else if ((match = AksNodePool.Match(resourceId)).Success)
            {
                state = new AksNodePoolResourceState(resourceId, logger, resourceConfiguration, this.resourceLocationResolver, this.vmSizeResolver);
                PopulateResourceParts(state, match);
                return state;
            }
            else if ((match = FileShare.Match(resourceId)).Success)
            {
                state = new StorageFileShareResourceState(resourceId, logger, resourceConfiguration, this.resourceLocationResolver, this.vmSizeResolver);
                PopulateResourceParts(state, match);
                return state;
            }
            else if ((match = FabricCapacity.Match(resourceId)).Success)
            {
                state = new FabricCapacityResourceState(resourceId, logger, resourceConfiguration, this.resourceLocationResolver, this.vmSizeResolver);
                PopulateResourceParts(state, match);
                return state;
            }
            else if ((match = AzureDevOpsParallelJobs.Match(resourceId)).Success)
            {
                state = new AzureDevOpsParallelJobsResourceState(resourceId, logger, resourceConfiguration, resourceInstance);
                PopulateResourceParts(state, match);
                return state;
            }

            throw new ArgumentException($"Unsupported resource type: {resourceId}");
        }

        /// <summary>
        /// Populates the resource state's ResourceParts from the regex match groups.
        /// </summary>
        /// <param name="state">The resource state to populate.</param>
        /// <param name="match">The regex match containing named groups.</param>
        private static void PopulateResourceParts(ResourceState state, Match match)
        {
            foreach (Group group in match.Groups)
            {
                if (group.Name != "0") // Skip the full match group
                {
                    state.ResourceParts[group.Name] = group.Value;
                }
            }
        }
    }
}
