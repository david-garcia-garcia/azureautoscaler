using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.Storage;
using Microsoft.Extensions.Logging;
using poolautoscaler.resourcemanagement;

namespace poolautoscaler.resources.StorageFileShare
{
    /// <summary>
    /// Helper for storage file share quota, IOPS and throughput calculations.
    /// </summary>
    public static class StorageFileShareResourceStateHelper
    {
        /// <summary>Gets the IOPS for a given quota in GB.</summary>
        /// <param name="quotaGb">Share quota in GB.</param>
        /// <returns>IOPS value.</returns>
        public static int GetIopsFromQuota(int quotaGb)
        {
            return Math.Min(3000 + quotaGb, 102400);
        }

        /// <summary>Gets the throughput (MiB/s) for a given quota in GB.</summary>
        /// <param name="quotaGb">Share quota in GB.</param>
        /// <returns>Throughput in MiB/s.</returns>
        public static double GetThroughputFromQuota(int quotaGb)
        {
            var throughput = 100 + Math.Ceiling(0.04 * quotaGb) + Math.Ceiling(0.06 * quotaGb);
            return throughput;
        }

        /// <summary>Gets the minimum quota (GB) needed to achieve the target throughput.</summary>
        /// <param name="targetThroughputMbps">Target throughput in MiB/s.</param>
        /// <param name="quotaIncrementGb">Quota increment in GB for the search.</param>
        /// <returns>Quota in GB.</returns>
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

        /// <summary>Expands a file share resource ID that may contain wildcards into concrete resource IDs.</summary>
        /// <param name="client">The ARM client.</param>
        /// <param name="key">The key for the resource entry.</param>
        /// <param name="resourceId">The resource ID or wildcard pattern.</param>
        /// <param name="logger">The logger.</param>
        /// <param name="stoppingToken">Cancellation token.</param>
        /// <returns>A dictionary of key to expanded resource IDs.</returns>
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
