using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.Storage;
using Microsoft.Extensions.Logging;
using poolautoscaler.resourcemanagement;

namespace poolautoscaler.resources.StorageFileShare
{
    public static class StorageFileShareResourceStateHelper
    {
        public static int GetIopsFromQuota(int quotaGb)
        {
            return Math.Min(3000 + quotaGb, 102400);
        }

        public static double GetThroughputFromQuota(int quotaGb)
        {
            var throughput = 100 + Math.Ceiling(0.04 * quotaGb) + Math.Ceiling(0.06 * quotaGb);
            return throughput;
        }

        public static int GetQuotaFromThroughput(double targetThroughputMbps, int quotaIncrementGb = 10)
        {
            for (int quota = 100; quota <= 102400; quota += quotaIncrementGb)
            {
                var availableThroughput = GetThroughputFromQuota(quota);
                if (availableThroughput >= targetThroughputMbps)
                {
                    return quota;
                }
            }

            return 102400;
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

            var storageAccountId = $"/subscriptions/{match.Groups["subscriptionId"].Value}/resourceGroups/{match.Groups["resourceGroupName"].Value}/providers/Microsoft.Storage/storageAccounts/{match.Groups["storageAccountName"].Value}";

            var storageAccount = await client.GetStorageAccountResource(new ResourceIdentifier(storageAccountId)).GetAsync(cancellationToken: stoppingToken);

            var fileShares = storageAccount.Value.GetFileService().GetFileShares();

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
