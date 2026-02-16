using Azure.ResourceManager;
using Azure.ResourceManager.Resources;

namespace poolautoscaler.resourcemanagement
{
    /// <summary>
    /// Wraps an ARM client and provides cached access to the tenant resource.
    /// Avoids repeated calls to <see cref="ArmClient.GetTenants"/> for change history queries.
    /// </summary>
    public interface IArmClientWrapper
    {
        /// <summary>
        /// Gets the underlying ARM client.
        /// </summary>
        ArmClient Client { get; }

        /// <summary>
        /// Gets the tenant resource, cached after the first call.
        /// </summary>
        /// <returns>The first tenant, or null if none.</returns>
        TenantResource? GetTenantResource();
    }
}
