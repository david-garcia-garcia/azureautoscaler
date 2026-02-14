using Azure.ResourceManager.Sql.Models;
using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.Sql;
using Microsoft.Extensions.Logging;
using poolautoscaler.resourcemanagement;

namespace poolautoscaler.resources.MssqlElasticPool
{
    public static class MssqlElasticPoolResourceStateHelper
    {
        public static readonly long[] StandardDtuCapacities = [50, 100, 200, 300, 400, 800, 1200, 1600, 2000, 2500, 3000];
        public static readonly long[] StandardDataMaxSize = [500, 750, 1024, 1280, 1536, 2048, 2560, 3072, 3584, 3840, 4096];

        public static readonly long[] PremiumDtuCapacities = [125, 250, 500, 1000, 1500, 2000, 2500, 3500, 4000];
        public static readonly long[] PremiumDataMaxSize = [1204, 1024, 1024, 1024, 1536, 2048, 2560, 3072, 3584, 4096];

        public static readonly long[] ValidStorageSizes =
        [
            50, 100, 150, 200, 250, 300, 400, 500, 750, 800,
            1024, 1200, 1280, 1536, 1600, 1792, 2000, 2048,
            2304, 2500, 2560, 2816, 3000, 3072, 3328, 3584,
            3840, 4096
        ];

        public static long FindClosesValidStorageSize(long sizeBytes)
        {
            for (int x = 0; x < ValidStorageSizes.Length; x++)
            {
                var s = (long)ValidStorageSizes[x] * 1024 * 1024 * 1024;
                if (s >= sizeBytes)
                {
                    return s;
                }
            }

            throw new Exception("Could not find any storage size.");
        }

        public static (long dtu, long capacity) FindClosestDtuThatCanHoldStorage(SqlSku sku, int dtu, long storageBytes)
        {
            var capacityValues = GetCapacityValues(sku);
            var maxDataSizeValues = GetStorageCapacityValues(sku);
            int startPosition;
            for (startPosition = 0; startPosition < capacityValues.Length; startPosition++)
            {
                if (capacityValues[startPosition] >= dtu)
                {
                    break;
                }
            }

            for (int i = startPosition; i < capacityValues.Length; i++)
            {
                if ((long)maxDataSizeValues[i] * 1024 * 1024 * 1204 >= storageBytes)
                {
                    return (capacityValues[i], maxDataSizeValues[i]);
                }
            }

            throw new Exception("No tier can accomodate DTU and/or capacity request.");
        }

        public static long[] GetCapacityValues(SqlSku sku)
        {
            switch (sku.Name)
            {
                case "StandardPool": return StandardDtuCapacities;
                case "PremiumPool": return PremiumDtuCapacities;
                default: throw new ArgumentException("Invalid SKU.");
            }
        }

        public static long[] GetStorageCapacityValues(SqlSku sku)
        {
            switch (sku.Name)
            {
                case "StandardPool": return StandardDataMaxSize;
                case "PremiumPool": return PremiumDataMaxSize;
                default: throw new ArgumentException("Invalid SKU.");
            }
        }

        public static async Task<Dictionary<string, string>> ExpandElasticPoolWildcard(ArmClient client, string key, string resourceId, ILogger logger, CancellationToken stoppingToken)
        {
            var result = new Dictionary<string, string>();
            var match = ResourceStateFactory.ElasticPools.Match(resourceId);
            if (!match.Success)
            {
                throw new ArgumentException("Invalid Elastic Pool resource ID", nameof(resourceId));
            }

            var elasticPoolName = match.Groups["elasticPoolName"].Value;
            var pattern = ResourceStateFactory.GetResourcePattern(elasticPoolName);
            if (pattern == null)
            {
                result.Add(key, resourceId);
                return result;
            }

            logger.LogDebug("Expanding resource wildcard: " + resourceId);
            var serverId = $"/subscriptions/{match.Groups["subscriptionId"].Value}/resourceGroups/{match.Groups["resourceGroupName"].Value}/providers/Microsoft.Sql/servers/{match.Groups["serverName"].Value}";
            var server = await client.GetSqlServerResource(new ResourceIdentifier(serverId)).GetAsync(cancellationToken: stoppingToken);
            var elasticPools = server.Value.GetElasticPools();
            await foreach (var elasticPool in elasticPools)
            {
                if (pattern.IsMatch(elasticPool.Data.Name))
                {
                    var expandedId = $"{serverId}/elasticPools/{elasticPool.Data.Name}";
                    result.Add(key + "_" + elasticPool.Data.Name, expandedId);
                    logger.LogDebug("Expanded elastic pool: {0}", expandedId);
                }
            }

            return result;
        }
    }
}
