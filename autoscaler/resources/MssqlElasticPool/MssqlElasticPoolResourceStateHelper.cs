using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.Sql;
using Azure.ResourceManager.Sql.Models;
using Microsoft.Extensions.Logging;
using poolautoscaler.configuration;
using poolautoscaler.resourcemanagement;
using poolautoscaler.resourcemanagement.Dto;

namespace poolautoscaler.resources.MssqlElasticPool
{
    /// <summary>
    /// Helper for SQL Elastic Pool DTU, storage sizes and wildcard expansion.
    /// </summary>
    public static class MssqlElasticPoolResourceStateHelper
    {
        /// <summary>Standard tier DTU capacity options for elastic pools.</summary>
        public static readonly long[] StandardDtuCapacities = [50, 100, 200, 300, 400, 800, 1200, 1600, 2000, 2500, 3000];

        /// <summary>Standard tier max data sizes (GB) per DTU capacity index.</summary>
        public static readonly long[] StandardDataMaxSize = [500, 750, 1024, 1280, 1536, 2048, 2560, 3072, 3584, 3840, 4096];

        /// <summary>Premium tier DTU capacity options for elastic pools.</summary>
        public static readonly long[] PremiumDtuCapacities = [125, 250, 500, 1000, 1500, 2000, 2500, 3500, 4000];

        /// <summary>Premium tier max data sizes (GB) per DTU capacity index.</summary>
        public static readonly long[] PremiumDataMaxSize = [1204, 1024, 1024, 1024, 1536, 2048, 2560, 3072, 3584, 4096];

        /// <summary>Standard tier per-database max eDTU options (filtered by pool eDTU).</summary>
        public static readonly int[] StandardPerDbMaxCapacities = [10, 20, 50, 100, 200, 300, 400, 800, 1200, 1600, 2000, 2500, 3000];

        /// <summary>Premium tier per-database max eDTU options (filtered by pool-size ceiling).</summary>
        public static readonly int[] PremiumPerDbMaxCapacities = [25, 50, 75, 125, 250, 500, 1000, 1750, 4000];

        /// <summary>Pool eDTU to effective per-database max eDTU ceiling for Premium pools.</summary>
        public static readonly IReadOnlyDictionary<int, int> PremiumPerDbCeiling =
            new Dictionary<int, int>
            {
                [125] = 125,
                [250] = 250,
                [500] = 500,
                [1000] = 1000,
                [1500] = 1000,
                [2000] = 1750,
                [2500] = 1750,
                [3000] = 1750,
                [3500] = 1750,
                [4000] = 4000,
            };

        /// <summary>Valid elastic pool storage sizes in GB.</summary>
        public static readonly long[] ValidStorageSizes =
        [
            50, 100, 150, 200, 250, 300, 400, 500, 750, 800,
            1024, 1200, 1280, 1536, 1600, 1792, 2000, 2048,
            2304, 2500, 2560, 2816, 3000, 3072, 3328, 3584,
            3840, 4096
        ];

        /// <summary>Finds the closest valid storage size (in bytes) that is at least sizeBytes.</summary>
        /// <param name="sizeBytes">Desired size in bytes.</param>
        /// <returns>Valid storage size in bytes.</returns>
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

        /// <summary>Finds the closest DTU tier that can hold the given storage size.</summary>
        /// <param name="sku">The SQL SKU.</param>
        /// <param name="dtu">Minimum DTU.</param>
        /// <param name="storageBytes">Required storage in bytes.</param>
        /// <returns>Tuple of (Dtu, Capacity) in maxCapacityBytes.</returns>
        public static (long Dtu, long Capacity) FindClosestDtuThatCanHoldStorage(SqlSku sku, int dtu, long storageBytes)
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

        /// <summary>
        /// Snaps an arbitrary DTU value to the nearest valid capacity tier (rounded up).
        /// If the value exceeds all tiers, the highest tier is returned.
        /// </summary>
        /// <param name="sku">The SQL SKU (StandardPool or PremiumPool).</param>
        /// <param name="value">The raw DTU value to snap.</param>
        /// <returns>The nearest valid DTU capacity that is >= <paramref name="value"/>.</returns>
        public static long SnapToNearestCapacity(SqlSku sku, long value)
        {
            var capacityValues = GetCapacityValues(sku);
            foreach (var capacity in capacityValues)
            {
                if (capacity >= value)
                {
                    return capacity;
                }
            }

            return capacityValues[capacityValues.Length - 1];
        }

        /// <summary>Gets DTU capacity values for the given elastic pool SKU.</summary>
        /// <param name="sku">The SQL SKU (StandardPool or PremiumPool).</param>
        /// <returns>Array of DTU capacities.</returns>
        public static long[] GetCapacityValues(SqlSku sku)
        {
            switch (sku.Name)
            {
                case "StandardPool": return StandardDtuCapacities;
                case "PremiumPool": return PremiumDtuCapacities;
                default: throw new ArgumentException($"Invalid elastic pool SKU for DTU scaling: Name='{sku.Name}'. Expected StandardPool or PremiumPool.");
            }
        }

        /// <summary>Gets max storage capacity values (GB) for the given elastic pool SKU.</summary>
        /// <param name="sku">The SQL SKU.</param>
        /// <returns>Array of max data sizes in GB.</returns>
        public static long[] GetStorageCapacityValues(SqlSku sku)
        {
            switch (sku.Name)
            {
                case "StandardPool": return StandardDataMaxSize;
                case "PremiumPool": return PremiumDataMaxSize;
                default: throw new ArgumentException($"Invalid elastic pool SKU for storage capacity: Name='{sku.Name}'. Expected StandardPool or PremiumPool.");
            }
        }

        /// <summary>Gets allowed per-database max eDTU values for the pool SKU and pool eDTU.</summary>
        /// <param name="sku">The elastic pool SQL SKU (StandardPool or PremiumPool).</param>
        /// <param name="poolDtu">The pool&apos;s eDTU capacity.</param>
        /// <returns>Allowed per-database max values, sorted ascending.</returns>
        public static int[] GetPerDbMaxCapacityValues(SqlSku sku, int poolDtu)
        {
            switch (sku.Name)
            {
                case "StandardPool":
                    return StandardPerDbMaxCapacities.Where(v => v <= poolDtu).ToArray();
                case "PremiumPool":
                    if (!PremiumPerDbCeiling.TryGetValue(poolDtu, out int ceiling))
                    {
                        ceiling = poolDtu;
                    }

                    return PremiumPerDbMaxCapacities.Where(v => v <= ceiling).ToArray();
                default:
                    throw new ArgumentException($"Invalid elastic pool SKU for per-database max capacity: Name='{sku.Name}'. Expected StandardPool or PremiumPool.");
            }
        }

        /// <summary>
        /// Snaps a per-database max eDTU request to the smallest allowed value that is &gt;= <paramref name="value"/>, or the maximum allowed if <paramref name="value"/> exceeds all tiers.
        /// </summary>
        /// <param name="sku">The elastic pool SQL SKU.</param>
        /// <param name="poolDtu">The pool eDTU.</param>
        /// <param name="value">Requested per-database max eDTU.</param>
        /// <returns>The snapped per-database max eDTU.</returns>
        public static int SnapToNearestPerDbMaxCapacity(SqlSku sku, int poolDtu, int value)
        {
            var values = GetPerDbMaxCapacityValues(sku, poolDtu);
            if (values.Length == 0)
            {
                throw new ArgumentException($"No per-database max eDTU tiers for pool SKU '{sku.Name}' at {poolDtu} eDTU.");
            }

            foreach (var v in values)
            {
                if (v >= value)
                {
                    return v;
                }
            }

            return values[values.Length - 1];
        }

        /// <summary>Expands an elastic pool resource ID that may contain wildcards into concrete resource IDs with filter contexts.</summary>
        /// <param name="client">The ARM client.</param>
        /// <param name="key">The key for the resource entry.</param>
        /// <param name="resourceId">The resource ID or wildcard pattern.</param>
        /// <param name="logger">The logger.</param>
        /// <param name="stoppingToken">Cancellation token.</param>
        /// <returns>A dictionary of key to <see cref="ExpandedResource"/>.</returns>
        public static async Task<Dictionary<string, ExpandedResource>> ExpandElasticPoolWildcard(ArmClient client, string key, string resourceId, ILogger logger, CancellationToken stoppingToken)
        {
            var result = new Dictionary<string, ExpandedResource>();
            var match = ResourceStateFactory.ElasticPools.Match(resourceId);
            if (!match.Success)
            {
                throw new ArgumentException("Invalid Elastic Pool resource ID", nameof(resourceId));
            }

            var elasticPoolName = match.Groups["elasticPoolName"].Value;
            var pattern = ResourceStateFactory.GetResourcePattern(elasticPoolName);
            if (pattern == null)
            {
                result.Add(key, new ExpandedResource { ResourceId = resourceId, Context = null });
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
                    var context = new ResourceFilterContext
                    {
                        ResourceName = elasticPool.Data.Name,
                        Tags = elasticPool.Data.Tags ?? new Dictionary<string, string>(),
                        Resource = elasticPool,
                    };
                    result.Add(key + "_" + elasticPool.Data.Name, new ExpandedResource { ResourceId = expandedId, Context = context });
                    logger.LogDebug("Expanded elastic pool: {0}", expandedId);
                }
            }

            return result;
        }
    }
}
