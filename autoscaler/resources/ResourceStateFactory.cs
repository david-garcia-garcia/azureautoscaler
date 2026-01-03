using System.Text.RegularExpressions;
using Azure.ResourceManager;
using Microsoft.Extensions.Logging;

namespace poolautoscaler.resources
{
    public static class ResourceStateFactory
    {
        public static readonly Regex ElasticPools = new Regex(@"^/subscriptions/(?<subscriptionId>[^/]+)/resourceGroups/(?<resourceGroupName>[^/]+)/providers/Microsoft.Sql/servers/(?<serverName>[^/]+)/elasticPools/(?<elasticPoolName>[^/]+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        public static readonly Regex SqlDatabase = new Regex(@"^/subscriptions/(?<subscriptionId>[^/]+)/resourceGroups/(?<resourceGroupName>[^/]+)/providers/Microsoft.Sql/servers/(?<serverName>[^/]+)/databases/(?<databaseName>[^/]+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        public static readonly Regex MySqlFlexibleServer = new Regex(@"^/subscriptions/(?<subscriptionId>[^/]+)/resourceGroups/(?<resourceGroupName>[^/]+)/providers/Microsoft.DBforMySQL/flexibleServers/(?<serverName>[^/]+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        public static readonly Regex AksNodePool = new Regex(@"^/subscriptions/(?<subscriptionId>[^/]+)/resourceGroups/(?<resourceGroupName>[^/]+)/providers/Microsoft.ContainerService/managedClusters/(?<clusterName>[^/]+)/agentPools/(?<nodePoolName>[^/]+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        public static readonly Regex FileShare = new Regex(@"^/subscriptions/(?<subscriptionId>[^/]+)/resourceGroups/(?<resourceGroupName>[^/]+)/providers/Microsoft.Storage/storageAccounts/(?<storageAccountName>[^/]+)/fileServices/default/shares/(?<fileShareName>[^/]+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex RegexPattern = new Regex(@"\{(.*?)\}", RegexOptions.Compiled);

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

        public static async Task<Dictionary<string, string>> ExpandResources(ArmClient client, string key, string resourceId, ILogger logger, CancellationToken stoppingToken)
        {
            // Make sure these are ordered from most specific to least specific
            Match match;
            ResourceState state;

            if ((match = ElasticPools.Match(resourceId)).Success)
            {
                return await MssqlElasticPoolResourceStateHelper.ExpandElasticPoolWildcard(client, key, resourceId, logger, stoppingToken);
            }
            else if ((match = SqlDatabase.Match(resourceId)).Success)
            {
                return await MsSqlDatabaseResourceStateHelper.ExpandSqlDatabaseWildcard(client, key, resourceId, logger, stoppingToken);
            }
            else if ((match = AksNodePool.Match(resourceId)).Success)
            {
                return await AksNodePoolResourceStateHelper.ExpandNodePoolWildcard(client, key, resourceId, logger, stoppingToken);
            }
            else if ((match = FileShare.Match(resourceId)).Success)
            {
                return await StorageFileShareResourceStateHelper.ExpandFileShareWildcard(client, key, resourceId, logger, stoppingToken);
            }

            var result = new Dictionary<string, string>();
            result.Add(key, resourceId);
            return result;
        }

        public static ResourceState Create(string resourceId, ILogger logger, Resource resourceConfiguration)
        {
            // Make sure these are ordered from most specific to least specific
            Match match;
            ResourceState state;

            if ((match = ElasticPools.Match(resourceId)).Success)
            {
                state = new MssqlElasticPoolResourceState(resourceId, logger, resourceConfiguration);
                PopulateResourceParts(state, match);
                return state;
            }
            else if ((match = SqlDatabase.Match(resourceId)).Success)
            {
                state = new MsSqlDatabaseResourceState(resourceId, logger, resourceConfiguration);
                PopulateResourceParts(state, match);
                return state;
            }
            else if ((match = MySqlFlexibleServer.Match(resourceId)).Success)
            {
                state = new MySqlFlexibleServerResourceState(resourceId, logger, resourceConfiguration);
                PopulateResourceParts(state, match);
                return state;
            }
            else if ((match = AksNodePool.Match(resourceId)).Success)
            {
                state = new AksNodePoolResourceState(resourceId, logger, resourceConfiguration);
                PopulateResourceParts(state, match);
                return state;
            }
            else if ((match = FileShare.Match(resourceId)).Success)
            {
                state = new StorageFileShareResourceState(resourceId, logger, resourceConfiguration);
                PopulateResourceParts(state, match);
                return state;
            }

            throw new ArgumentException($"Unsupported resource type: {resourceId}");
        }

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