using Azure.Core;
using Azure.ResourceManager;

namespace poolautoscaler.metrics.Dto
{
    /// <summary>Helper functions available in custom metric DataExpression.</summary>
    public class CustomMetricHelpers
    {
        private readonly string subscriptionId;
        private readonly AzureLocation location;
        private readonly IVmSizeResolver vmSizeResolver;
        private readonly ArmClient armClient;
        private readonly CancellationToken cancellationToken;

        /// <summary>Initializes a new instance of the <see cref="CustomMetricHelpers"/> class with VM size resolver (lazy-loads from Azure when used).</summary>
        /// <param name="subscriptionId">The subscription ID.</param>
        /// <param name="location">The Azure location.</param>
        /// <param name="vmSizeResolver">The VM size resolver.</param>
        /// <param name="armClient">The ARM client (used by resolver to load VM sizes on first use).</param>
        /// <param name="cancellationToken">Cancellation token for load operations.</param>
        public CustomMetricHelpers(string subscriptionId, AzureLocation location, IVmSizeResolver vmSizeResolver, ArmClient armClient, CancellationToken cancellationToken = default)
        {
            this.subscriptionId = subscriptionId;
            this.location = location;
            this.vmSizeResolver = vmSizeResolver;
            this.armClient = armClient;
            this.cancellationToken = cancellationToken;
        }

        /// <summary>Gets memory in bytes for a VM size (e.g. Standard_D4s_v3).</summary>
        /// <param name="vmSize">The VM SKU name.</param>
        /// <returns>Memory in bytes.</returns>
        public long VmSizeToMemory(string vmSize)
        {
            return this.vmSizeResolver.GetMemoryBytes(this.subscriptionId, this.location, vmSize, this.armClient, this.cancellationToken);
        }

        /// <summary>Gets memory in GiB for a VM size (bytes converted to GiB, e.g. Standard_D4s_v3).</summary>
        /// <param name="vmSize">The VM SKU name.</param>
        /// <returns>Memory in GiB as a double.</returns>
        public double VmSizeToMemoryGb(string vmSize)
        {
            var bytes = this.VmSizeToMemory(vmSize);
            return bytes / (1024.0 * 1024 * 1024);
        }

        /// <summary>Gets core count for a VM size (e.g. Standard_D4s_v3 -> 4).</summary>
        /// <param name="vmSize">The VM SKU name.</param>
        /// <returns>Core count.</returns>
        public int VmSizeToCores(string vmSize)
        {
            return this.vmSizeResolver.GetCoreCount(this.subscriptionId, this.location, vmSize, this.armClient, this.cancellationToken);
        }
    }
}
