using Azure.ResourceManager;
using Azure.ResourceManager.Resources;

namespace poolautoscaler.resourcemanagement
{
    /// <summary>
    /// Implementation of <see cref="IArmClientWrapper"/> that caches the tenant resource.
    /// </summary>
    public class ArmClientWrapper : IArmClientWrapper
    {
        private readonly object lockObj = new();
        private TenantResource? cachedTenant;

        /// <summary>
        /// Initializes a new instance of the <see cref="ArmClientWrapper"/> class.
        /// </summary>
        /// <param name="client">The ARM client to wrap.</param>
        public ArmClientWrapper(ArmClient client)
        {
            this.Client = client;
        }

        /// <inheritdoc />
        public ArmClient Client { get; }

        /// <inheritdoc />
        public TenantResource? GetTenantResource()
        {
            if (this.cachedTenant != null)
            {
                return this.cachedTenant;
            }

            lock (this.lockObj)
            {
                if (this.cachedTenant != null)
                {
                    return this.cachedTenant;
                }

                this.cachedTenant = this.Client.GetTenants().FirstOrDefault();
                return this.cachedTenant;
            }
        }
    }
}
