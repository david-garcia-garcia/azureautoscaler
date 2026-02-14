using Azure.ResourceManager.Sql.Models;
using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.Sql;
using Microsoft.Extensions.Logging;
using poolautoscaler.resourcemanagement;

namespace poolautoscaler.resources.MsSqlDatabase
{
    public static class MsSqlDatabaseResourceStateHelper
    {
        public static readonly int[] StandardDtuCapacities = [10, 20, 50, 100, 200, 400, 800, 1600, 3000];
        public static readonly long[] StandardDataMaxSize = [250, 250, 250, 1024, 1024, 1024, 1024, 1024, 1024];

        public static readonly int[] PremiumDtuCapacities = [125, 250, 500, 1000, 1750, 4000];
        public static readonly long[] PremiumDataMaxSize = [1204, 1024, 1024, 1024, 4096, 4096];

        public static readonly double[] DtuBasicValidStorage = [0.1, 0.5, 1, 2];
        public static readonly double[] DtuElasticPoolValidStorage = [0.1, 0.5, 1, 2, 5, 10, 20, 30, 40, 50, 100, 150, 200, 250, 300, 400, 500, 750, 1024];
        public static readonly double[] DtuStandardValidStorage = [0.1, 0.5, 1, 2, 5, 10, 20, 30, 40, 50, 100, 150, 200, 250];
        public static readonly double[] DtuPremiumValidStorage = [0.1, 0.5, 1, 2, 5, 10, 20, 30, 40, 50, 100, 150, 200, 250, 300, 400, 500, 750, 1024];

        public static bool IsDtuModel(SqlSku sku)
        {
            return sku.Name == "Basic" || sku.Name == "Standard" || sku.Name == "Premium" || sku.Name == "ElasticPool";
        }

        public static double[] GetValidStorageSizesForDatabase(SqlSku sku)
        {
            if (IsDtuModel(sku))
            {
                if (sku.Name == "ElasticPool")
                {
                    return DtuElasticPoolValidStorage;
                }

                if (sku.Name == "Basic")
                {
                    return DtuBasicValidStorage;
                }

                if (sku.Name == "Standard")
                {
                    return DtuStandardValidStorage;
                }

                if (sku.Name == "Premium")
                {
                    return DtuPremiumValidStorage;
                }
            }
            else
            {
                var skuName = sku.Name ?? "";
                var family = sku.Family ?? "";
                var tier = sku.Tier?.ToString() ?? "";
                skuName = skuName.ToUpperInvariant();
                family = family.ToUpperInvariant();
                tier = tier.ToUpperInvariant();

                if (skuName.Contains("HS") || tier.Contains("HYPERSCALE"))
                {
                    throw new NotSupportedException("MaxDataBytes dimension is not supported for Hyperscale service tier");
                }

                bool isServerless = family == "S" || skuName.Contains("_S_") || skuName.Contains("SERVERLESS");

                if ((skuName.Contains("BC") || tier.Contains("BUSINESSCRITICAL")) && isServerless)
                {
                    throw new NotSupportedException("MaxDataBytes dimension is not supported for Business Critical Serverless");
                }

                if ((skuName.Contains("GP") || tier.Contains("GENERALPURPOSE")) && !isServerless)
                {
                    var validSizes = new List<double>();
                    for (int i = 1; i <= 1024; i++)
                    {
                        validSizes.Add(i);
                    }

                    return validSizes.ToArray();
                }

                if ((skuName.Contains("GP") || tier.Contains("GENERALPURPOSE")) && isServerless)
                {
                    var validSizes = new List<double>();
                    for (int i = 1; i <= 512; i++)
                    {
                        validSizes.Add(i);
                    }

                    return validSizes.ToArray();
                }

                if ((skuName.Contains("BC") || tier.Contains("BUSINESSCRITICAL")) && !isServerless)
                {
                    var validSizes = new List<double>();
                    for (int i = 1; i <= 1024; i++)
                    {
                        validSizes.Add(i);
                    }

                    return validSizes.ToArray();
                }

                throw new ArgumentException($"Could not determine valid storage sizes for SKU: Name={sku.Name}, Tier={tier}, Family={family}");
            }

            throw new ArgumentException($"Could not determine valid storage sizes for SKU: {sku.Name}");
        }

        public static long FindClosestValidStorageSizeForDatabase(long sizeBytes, SqlSku sku)
        {
            var validSizesGb = GetValidStorageSizesForDatabase(sku);
            var sizeGb = (double)sizeBytes / (1024 * 1024 * 1024);
            foreach (var validSizeGb in validSizesGb)
            {
                if (validSizeGb >= sizeGb)
                {
                    return (long)(validSizeGb * 1024 * 1024 * 1024);
                }
            }

            throw new Exception($"Could not find valid storage size for {sizeGb} GB with SKU {sku.Name}");
        }

        public static long GetNextValidStorageSizeForDatabase(long sizeBytes, SqlSku sku)
        {
            var validSizesGb = GetValidStorageSizesForDatabase(sku);
            var sizeGb = (double)sizeBytes / (1024 * 1024 * 1024);
            if (!IsDtuModel(sku))
            {
                var currentGb = (int)Math.Ceiling(sizeGb);
                var maxGb = (int)validSizesGb[validSizesGb.Length - 1];
                if (currentGb < maxGb)
                {
                    return (long)((currentGb + 1) * 1024 * 1024 * 1024);
                }

                return (long)(maxGb * 1024 * 1024 * 1024);
            }

            for (int i = 0; i < validSizesGb.Length; i++)
            {
                if (validSizesGb[i] >= sizeGb)
                {
                    if (i < validSizesGb.Length - 1)
                    {
                        return (long)(validSizesGb[i + 1] * 1024 * 1024 * 1024);
                    }

                    return (long)(validSizesGb[i] * 1024 * 1024 * 1024);
                }
            }

            throw new Exception($"Could not find next storage size for {sizeGb} GB with SKU {sku.Name}");
        }

        public static long GetPreviousValidStorageSizeForDatabase(long sizeBytes, SqlSku sku)
        {
            var validSizesGb = GetValidStorageSizesForDatabase(sku);
            var sizeGb = (double)sizeBytes / (1024 * 1024 * 1024);
            if (!IsDtuModel(sku))
            {
                var currentGb = (int)Math.Floor(sizeGb);
                var minGb = (int)validSizesGb[0];
                if (currentGb > minGb)
                {
                    return (long)((currentGb - 1) * 1024 * 1024 * 1024);
                }

                return (long)(minGb * 1024 * 1024 * 1024);
            }

            for (int i = validSizesGb.Length - 1; i >= 0; i--)
            {
                if (validSizesGb[i] <= sizeGb)
                {
                    if (i > 0)
                    {
                        return (long)(validSizesGb[i - 1] * 1024 * 1024 * 1024);
                    }

                    return (long)(validSizesGb[i] * 1024 * 1024 * 1024);
                }
            }

            throw new Exception($"Could not find previous storage size for {sizeGb} GB with SKU {sku.Name}");
        }

        public static int[] GetCapacityValues(SqlSku sku)
        {
            switch (sku.Name)
            {
                case "Standard": return StandardDtuCapacities;
                case "Premium": return PremiumDtuCapacities;
                default: throw new ArgumentException($"Invalid SKU {sku.Name}");
            }
        }

        public static long[] GetStorageCapacityValues(SqlSku sku)
        {
            switch (sku.Name)
            {
                case "Standard": return StandardDataMaxSize;
                case "Premium": return PremiumDataMaxSize;
                default: throw new ArgumentException($"Invalid SKU {sku.Name}");
            }
        }

        public static (long dtu, long capacity) FindClosestDtuThatCanHoldStorage(SqlSku sku, int dtu, long storageBytes)
        {
            var capacityValues = GetCapacityValues(sku);
            var maxDataSizeValues = GetStorageCapacityValues(sku);
            int startPosition;
            for (startPosition = 0; startPosition < capacityValues.Length; startPosition++)
            {
                if (capacityValues[startPosition] >= sku.Capacity)
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

        public static async Task<Dictionary<string, string>> ExpandSqlDatabaseWildcard(ArmClient client, string key, string resourceId, ILogger logger, CancellationToken stoppingToken)
        {
            var result = new Dictionary<string, string>();
            var match = ResourceStateFactory.SqlDatabase.Match(resourceId);
            if (!match.Success)
            {
                throw new ArgumentException("Invalid SQL Database resource ID", nameof(resourceId));
            }

            var databaseName = match.Groups["databaseName"].Value;
            var pattern = ResourceStateFactory.GetResourcePattern(databaseName);
            if (pattern == null)
            {
                result.Add(key, resourceId);
                return result;
            }

            logger.LogDebug("Expanding resource wildcard: " + resourceId);
            var serverId = $"/subscriptions/{match.Groups["subscriptionId"].Value}/resourceGroups/{match.Groups["resourceGroupName"].Value}/providers/Microsoft.Sql/servers/{match.Groups["serverName"].Value}";
            var server = await client.GetSqlServerResource(new ResourceIdentifier(serverId)).GetAsync(cancellationToken: stoppingToken);
            var databases = server.Value.GetSqlDatabases();
            await foreach (var database in databases)
            {
                if (database.Data.Name == "master")
                {
                    continue;
                }

                if (pattern.IsMatch(database.Data.Name))
                {
                    var expandedId = $"{serverId}/databases/{database.Data.Name}";
                    result.Add(key + "_" + database.Data.Name, expandedId);
                    logger.LogDebug("Expanded SQL database: {0}", expandedId);
                }
            }

            return result;
        }
    }
}
