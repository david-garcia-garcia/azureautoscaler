using System.Runtime.ExceptionServices;
using Azure.ResourceManager.Compute;
using Azure.ResourceManager.Fabric;
using Azure.ResourceManager.MySql.FlexibleServers;
using Azure.ResourceManager.PostgreSql.FlexibleServers;
using Azure.ResourceManager.Sql;
using Azure.ResourceManager.Storage;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace poolautoscaler.resourcemanagement
{
    /// <summary>
    /// Resolves resource IDs to Azure region names using ARM and <see cref="IMemoryCache"/> (1-hour TTL).
    /// </summary>
    public sealed class ResourceLocationResolver : IResourceLocationResolver
    {
        private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

        private readonly IMemoryCache cache;
        private readonly ILogger logger;

        /// <summary>Initializes a new instance of the <see cref="ResourceLocationResolver"/> class.</summary>
        /// <param name="cache">The memory cache (e.g. from <c>AddMemoryCache</c>).</param>
        /// <param name="logger">The logger.</param>
        public ResourceLocationResolver(IMemoryCache cache, ILogger logger)
        {
            this.cache = cache;
            this.logger = logger;
        }

        /// <inheritdoc />
        public async Task<string?> GetRegionAsync(Azure.ResourceManager.ArmClient client, string resourceId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(resourceId))
            {
                return null;
            }

            try
            {
                var region = await this.cache.GetOrCreateAsync(
                    resourceId,
                    async entry =>
                    {
                        entry.AbsoluteExpirationRelativeToNow = CacheTtl;
                        return await this.FetchRegionAsync(client, resourceId, cancellationToken);
                    });

                if (string.IsNullOrEmpty(region))
                {
                    throw new Exception($"Failed to resolve region for resource: {resourceId}");
                }

                return region;
            }
            catch (Azure.RequestFailedException ex)
            {
                this.logger.LogWarning(ex, "Failed to resolve region for resource: {Message}", ex.Message);
                ExceptionDispatchInfo.Capture(ex).Throw();
                return null; // Unreachable; Throw() preserves stack trace and never returns.
            }
        }

        private async Task<string?> FetchRegionAsync(Azure.ResourceManager.ArmClient client, string resourceId, CancellationToken cancellationToken)
        {
            var id = new Azure.Core.ResourceIdentifier(resourceId);
            var resourceType = id.ResourceType.ToString();

            if (resourceType.Equals("Microsoft.Compute/virtualMachineScaleSets", StringComparison.OrdinalIgnoreCase))
            {
                var vmss = await client.GetVirtualMachineScaleSetResource(id).GetAsync(cancellationToken: cancellationToken);
                return vmss.Value.Data.Location.Name;
            }

            if (resourceType.Equals("Microsoft.DBforMySQL/flexibleServers", StringComparison.OrdinalIgnoreCase))
            {
                var server = await client.GetMySqlFlexibleServerResource(id).GetAsync(cancellationToken: cancellationToken);
                return server.Value.Data.Location.Name;
            }

            if (resourceType.Equals("Microsoft.DBforPostgreSQL/flexibleServers", StringComparison.OrdinalIgnoreCase))
            {
                var server = await client.GetPostgreSqlFlexibleServerResource(id).GetAsync(cancellationToken: cancellationToken);
                return server.Value.Data.Location.Name;
            }

            if (resourceType.Equals("Microsoft.ContainerService/managedClusters/agentPools", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (resourceType.Equals("Microsoft.Sql/servers/elasticPools", StringComparison.OrdinalIgnoreCase))
            {
                var pool = await client.GetElasticPoolResource(id).GetAsync(cancellationToken: cancellationToken);
                return pool.Value.Data.Location.Name;
            }

            if (resourceType.Equals("Microsoft.Sql/servers/databases", StringComparison.OrdinalIgnoreCase))
            {
                var db = await client.GetSqlDatabaseResource(id).GetAsync(cancellationToken: cancellationToken);
                return db.Value.Data.Location.Name;
            }

            if (resourceType.Equals("Microsoft.Storage/storageAccounts", StringComparison.OrdinalIgnoreCase))
            {
                var sa = await client.GetStorageAccountResource(id).GetAsync(cancellationToken: cancellationToken);
                return sa.Value.Data.Location.Name;
            }

            if (resourceType.Equals("Microsoft.Fabric/capacities", StringComparison.OrdinalIgnoreCase))
            {
                var capacity = await client.GetFabricCapacityResource(id).GetAsync(cancellationToken: cancellationToken);
                return capacity.Value.Data.Location.Name;
            }

            this.logger.LogDebug("No resolver for resource type: {ResourceType}", resourceType);
            return null;
        }
    }
}
