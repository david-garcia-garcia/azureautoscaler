using Azure.ResourceManager;
using Microsoft.Extensions.Logging;
using poolautoscaler.configuration;

namespace poolautoscaler.resourcemanagement
{
    /// <summary>
    /// Factory for creating and expanding <see cref="ResourceState"/> instances.
    /// Created states receive a reference to the service provider so they can resolve dependencies (e.g. resolvers) on demand.
    /// </summary>
    public interface IResourceStateFactory
    {
        /// <summary>
        /// Expands a resource ID that may contain wildcards into a dictionary of key to resource ID.
        /// </summary>
        /// <param name="client">The ARM client.</param>
        /// <param name="key">The key for the resource entry.</param>
        /// <param name="resourceId">The resource ID or wildcard pattern.</param>
        /// <param name="logger">The logger.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A dictionary of key to expanded resource IDs.</returns>
        Task<Dictionary<string, string>> ExpandResourcesAsync(
            ArmClient client,
            string key,
            string resourceId,
            ILogger logger,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates a <see cref="ResourceState"/> for the given resource ID and configuration.
        /// The returned state's <see cref="ResourceState.Services"/> is set so it can resolve services on demand.
        /// </summary>
        /// <param name="resourceId">The resource ID or URI.</param>
        /// <param name="logger">The logger.</param>
        /// <param name="resourceConfiguration">The resource configuration.</param>
        /// <param name="resourceInstance">Optional resource instance (e.g. for Azure DevOps).</param>
        /// <returns>A new resource state instance.</returns>
        ResourceState Create(
            string resourceId,
            ILogger logger,
            Resource resourceConfiguration,
            ResourceInstance? resourceInstance = null);
    }
}
