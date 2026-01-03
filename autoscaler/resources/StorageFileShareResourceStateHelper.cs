using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.Storage;
using Microsoft.Extensions.Logging;

namespace poolautoscaler.resources
{
    public static class StorageFileShareResourceStateHelper
    {
        /// <summary>
        /// Get IOPS based on provisioned storage quota (GiB)
        /// Uses Azure's official documented formula for Azure Files provisioned v1
        /// 
        /// Formula: MIN(3000 + 1 * ProvisionedStorageGiB, 102400)
        /// 
        /// NOTE: This implementation is designed for Azure Files provisioned v1 billing model where
        /// IOPS and throughput are determined by storage size. For provisioned v2 model,
        /// IOPS and throughput can be scaled independently and this calculation would not apply.
        /// </summary>
        public static int GetIopsFromQuota(int quotaGb)
        {
            // Azure's official formula for IOPS calculation
            // IOPS = MIN(3000 + 1 * ProvisionedStorageGiB, 102400)
            return Math.Min(3000 + quotaGb, 102400);
        }

        /// <summary>
        /// Get throughput (MiB/s) based on provisioned storage quota (GiB)
        /// Uses Azure's official documented formula for Azure Files provisioned v1
        /// 
        /// Formula: 100 + CEILING(0.04 * ProvisionedStorageGiB) + CEILING(0.06 * ProvisionedStorageGiB)
        /// 
        /// NOTE: This implementation is designed for Azure Files provisioned v1 billing model where
        /// IOPS and throughput are determined by storage size. For provisioned v2 model,
        /// IOPS and throughput can be scaled independently and this calculation would not apply.
        /// </summary>
        public static double GetThroughputFromQuota(int quotaGb)
        {
            // Azure's official formula for throughput calculation
            // Throughput (MiB/s) = 100 + CEILING(0.04 * ProvisionedStorageGiB) + CEILING(0.06 * ProvisionedStorageGiB)
            var throughput = 100 + Math.Ceiling(0.04 * quotaGb) + Math.Ceiling(0.06 * quotaGb);
            
            return throughput;
        }

        /// <summary>
        /// Get minimum quota (GB) required to achieve target throughput (MiB/s)
        /// 
        /// NOTE: This implementation is designed for Azure Files provisioned v1 billing model where
        /// IOPS and throughput are determined by storage size. For provisioned v2 model,
        /// IOPS and throughput can be scaled independently and this calculation would not apply.
        /// </summary>
        /// <param name="targetThroughputMbps">Target throughput in MiB/s</param>
        /// <param name="quotaIncrementGb">Quota increment in GB for the search (default: 10)</param>
        /// <returns>Minimum quota in GB that meets the throughput requirement</returns>
        public static int GetQuotaFromThroughput(double targetThroughputMbps, int quotaIncrementGb = 10)
        {
            // Start with minimum quota and find the smallest that meets the throughput requirement
            for (int quota = 100; quota <= 102400; quota += quotaIncrementGb) // Test up to 100 TB in specified increments
            {
                var availableThroughput = GetThroughputFromQuota(quota);
                if (availableThroughput >= targetThroughputMbps)
                {
                    return quota;
                }
            }

            // If we can't meet the throughput requirement, return maximum quota
            return 102400; // 100 TB maximum
        }

        public static async Task<Dictionary<string, string>> ExpandFileShareWildcard(ArmClient client, string key, string resourceId, ILogger logger, CancellationToken stoppingToken)
        {
            var result = new Dictionary<string, string>();

            var match = ResourceStateFactory.FileShare.Match(resourceId);
            if (!match.Success)
            {
                throw new ArgumentException("Invalid File Share resource ID", nameof(resourceId));
            }

            var fileShareName = match.Groups["fileShareName"].Value;
            var pattern = ResourceStateFactory.GetResourcePattern(fileShareName);
            if (pattern == null)
            {
                result.Add(key, resourceId);
                return result;
            }

            logger.LogDebug("Expanding resource wildcard: " + resourceId);

            // Extract storage account ID from the resource ID
            var storageAccountId = $"/subscriptions/{match.Groups["subscriptionId"].Value}/resourceGroups/{match.Groups["resourceGroupName"].Value}/providers/Microsoft.Storage/storageAccounts/{match.Groups["storageAccountName"].Value}";

            // Get the storage account
            var storageAccount = await client.GetStorageAccountResource(new ResourceIdentifier(storageAccountId)).GetAsync(cancellationToken: stoppingToken);

            // Get all file shares in the storage account
            var fileShares = storageAccount.Value.GetFileService().GetFileShares();

            // Build the list of resource IDs for each file share
            await foreach (var fileShare in fileShares)
            {
                if (pattern.IsMatch(fileShare.Data.Name))
                {
                    var expandedId = $"{storageAccountId}/fileServices/default/shares/{fileShare.Data.Name}";
                    result.Add(key + "_" + fileShare.Data.Name, expandedId);
                    logger.LogDebug("Expanded file share: {0}", expandedId);
                }
            }

            return result;
        }
    }
}