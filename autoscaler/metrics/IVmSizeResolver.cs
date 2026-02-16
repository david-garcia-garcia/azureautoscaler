using Azure.Core;

namespace poolautoscaler.metrics
{
    /// <summary>
    /// Resolves VM size names to memory and core count (e.g. for custom metrics).
    /// Uses Azure Virtual Machine Sizes API; implementations cache per location and
    /// lazy-load on first use when an ARM client is passed to the getters.
    /// </summary>
    public interface IVmSizeResolver
    {
        /// <summary>Gets memory in bytes for a VM size.</summary>
        /// <param name="subscriptionId">The subscription ID.</param>
        /// <param name="location">The Azure location.</param>
        /// <param name="vmSize">The VM SKU name (e.g. Standard_D4s_v3).</param>
        /// <param name="client">Optional ARM client; when provided, data is loaded on first use for that location.</param>
        /// <param name="cancellationToken">Cancellation token (used when loading).</param>
        /// <returns>Memory in bytes, or 0 if unknown.</returns>
        long GetMemoryBytes(
            string subscriptionId,
            AzureLocation location,
            string vmSize,
            Azure.ResourceManager.ArmClient? client = null,
            CancellationToken cancellationToken = default);

        /// <summary>Gets core count for a VM size.</summary>
        /// <param name="subscriptionId">The subscription ID.</param>
        /// <param name="location">The Azure location.</param>
        /// <param name="vmSize">The VM SKU name.</param>
        /// <param name="client">Optional ARM client; when provided, data is loaded on first use for that location.</param>
        /// <param name="cancellationToken">Cancellation token (used when loading).</param>
        /// <returns>Core count, or 0 if unknown.</returns>
        int GetCoreCount(
            string subscriptionId,
            AzureLocation location,
            string vmSize,
            Azure.ResourceManager.ArmClient? client = null,
            CancellationToken cancellationToken = default);
    }
}
