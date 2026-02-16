namespace poolautoscaler.resourcemanagement
{
    /// <summary>
    /// Resolves an Azure resource ID to its region name (e.g. "francecentral").
    /// Used for regional API endpoints (e.g. Azure Monitor metrics).
    /// </summary>
    public interface IResourceLocationResolver
    {
        /// <summary>
        /// Gets the region name for the given resource ID.
        /// Results are cached; cache is shared and has a TTL.
        /// </summary>
        /// <param name="client">The ARM client (used to fetch resource when not cached).</param>
        /// <param name="resourceId">The full resource ID (e.g. /subscriptions/.../resourceGroups/.../providers/...).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The region name (e.g. "francecentral"), or null if resolution fails.</returns>
        Task<string?> GetRegionAsync(Azure.ResourceManager.ArmClient client, string resourceId, CancellationToken cancellationToken);
    }
}
