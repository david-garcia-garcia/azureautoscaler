using Azure.Core;
using Azure.ResourceManager;

namespace poolautoscaler.metrics.Dto
{
    /// <summary>Helper functions available in custom metric DataExpression.</summary>
    public class CustomMetricHelpers
    {
        private readonly string subscriptionId;
        private readonly AzureLocation? location;
        private readonly IVmSizeResolver? vmSizeResolver;
        private readonly ArmClient? armClient;
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

        /// <summary>Initializes a new instance of the <see cref="CustomMetricHelpers"/> class without resolver (uses regex fallback for cores, returns 0 for memory).</summary>
        public CustomMetricHelpers()
        {
            this.subscriptionId = string.Empty;
            this.location = null;
            this.vmSizeResolver = null;
            this.armClient = null;
            this.cancellationToken = default;
        }

        /// <summary>Gets memory in bytes for a VM size (e.g. Standard_D4s_v3).</summary>
        /// <param name="vmSize">The VM SKU name.</param>
        /// <returns>Memory in bytes, or 0 if unknown.</returns>
        public long VmSizeToMemory(string vmSize)
        {
            if (this.vmSizeResolver != null && this.location.HasValue)
            {
                return this.vmSizeResolver.GetMemoryBytes(this.subscriptionId, this.location.Value, vmSize, this.armClient, this.cancellationToken);
            }

            return VmSizeInfo.GetMemoryBytes(vmSize);
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
        /// <returns>Core count, or 0 if unknown.</returns>
        public int VmSizeToCores(string vmSize)
        {
            if (this.vmSizeResolver != null && this.location.HasValue)
            {
                return this.vmSizeResolver.GetCoreCount(this.subscriptionId, this.location.Value, vmSize, this.armClient, this.cancellationToken);
            }

            return VmSizeInfo.GetCoreCount(vmSize);
        }
    }
}
