using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;

namespace poolautoscaler.resources
{
    /// <summary>
    /// Discovers and stores resource states. Runs expansion from configuration and keeps
    /// the current set of resources; call <see cref="DiscoverAsync"/> periodically to refresh.
    /// </summary>
    public class ResourceManager
    {
        private readonly Dictionary<string, ResourceState> _resources = new Dictionary<string, ResourceState>();
        private readonly ILogger _logger;
        private readonly ILoggerFactory _logFactory;
        private DateTime _lastDiscovery = DateTime.MinValue;

        public ResourceManager(ILoggerFactory logFactory)
        {
            _logFactory = logFactory;
            _logger = logFactory.CreateLogger("ResourceManager");
        }

        /// <summary>
        /// Current set of discovered resources (key: resource id, value: state). Do not modify the dictionary.
        /// </summary>
        public IReadOnlyDictionary<string, ResourceState> Resources => new ReadOnlyDictionary<string, ResourceState>(_resources);

        /// <summary>
        /// Runs resource discovery if <paramref name="discoveryFrequency"/> has elapsed since the last run.
        /// Call every loop iteration; discovery is performed only when due. First call always runs.
        /// </summary>
        /// <param name="client">ARM client for expansion calls.</param>
        /// <param name="resourceConfigs">Resource configuration (e.g. Configuration.Resources).</param>
        /// <param name="discoveryFrequency">Minimum interval between discovery runs.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task DiscoverAsync(
            Azure.ResourceManager.ArmClient client,
            IEnumerable<Resource> resourceConfigs,
            TimeSpan discoveryFrequency,
            CancellationToken cancellationToken = default)
        {
            if ((DateTime.UtcNow - _lastDiscovery) < discoveryFrequency)
                return;

            try
            {
                await DiscoverCoreAsync(client, resourceConfigs, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to discover resources: {Message}", ex.Message);
            }
            finally
            {
                _lastDiscovery = DateTime.UtcNow;
            }
        }

        /// <summary>
        /// Runs resource discovery immediately: expands configured resources via Azure, adds new ones and removes ones no longer present.
        /// </summary>
        private async Task DiscoverCoreAsync(
            Azure.ResourceManager.ArmClient client,
            IEnumerable<Resource> resourceConfigs,
            CancellationToken cancellationToken)
        {
            _logger.LogInformation("Performing resource discovery and expansion...");

            var discoveredResources = new HashSet<string>();
            int addedResources = 0;
            int removedResources = 0;

            if (resourceConfigs == null)
            {
                _logger.LogTrace("No resource configuration to discover.");
                return;
            }

            // Phase 1: Discover all resources that should exist and add new ones
            foreach (var resource in resourceConfigs)
            {
                if (resource.Enabled == false)
                {
                    _logger.LogTrace("Skipping disabled resource: {Ids}",
                        resource.Resources != null ? string.Join(",", resource.Resources.Values.Select(i => i.Id)) : "");
                    continue;
                }

                if (resource.Resources == null)
                    continue;

                foreach (var resourceInstance in resource.Resources)
                {
                    Dictionary<string, string> expandedResourceIds;
                    try
                    {
                        expandedResourceIds = await ResourceStateFactory.ExpandResources(
                            client,
                            resourceInstance.Key,
                            resourceInstance.Value.ResourceId,
                            _logger,
                            cancellationToken);
                    }
                    catch (Azure.RequestFailedException ex) when (ex.Status == 404)
                    {
                        _logger.LogError(
                            "Skipping deleted or non existing resource during expansion: {ResourceId}. Please remove this resource from your configuration.",
                            resourceInstance.Value.ResourceId);
                        continue;
                    }
                    catch (Azure.RequestFailedException ex) when (ex.Status == 403)
                    {
                        _logger.LogError(ex,
                            "Not authorized to access resource during expansion: {ResourceId}. Please check your credentials and permissions.",
                            resourceInstance.Value.ResourceId);
                        continue;
                    }

                    foreach (var expandedResourceId in expandedResourceIds)
                    {
                        resourceInstance.Value.Id = resourceInstance.Key;
                        discoveredResources.Add(expandedResourceId.Key);

                        if (_resources.ContainsKey(expandedResourceId.Key))
                        {
                            _logger.LogTrace("Keeping existing resource: {Id}", expandedResourceId.Value);
                        }
                        else
                        {
                            _logger.LogInformation("Adding new resource {Key}: {Id}", expandedResourceId.Key, expandedResourceId.Value);

                            var resourceLogger = _logFactory.CreateLogger(expandedResourceId.Key);
                            var state = ResourceStateFactory.Create(
                                expandedResourceId.Value,
                                resourceLogger,
                                resource,
                                resourceInstance.Value);
                            resourceLogger.LogDebug("Replacements: {Replacements}",
                                string.Join(", ", state.ResourceParts.Select((i) => $"{i.Key}={i.Value}")));
                            _resources[expandedResourceId.Key] = state;
                            addedResources++;
                        }
                    }
                }
            }

            // Phase 2: Cleanup - remove resources that no longer exist
            var resourcesToRemove = new List<string>();
            foreach (var existingResourceKey in _resources.Keys)
            {
                if (!discoveredResources.Contains(existingResourceKey))
                    resourcesToRemove.Add(existingResourceKey);
            }

            foreach (var resourceKey in resourcesToRemove)
            {
                _logger.LogInformation("Resource no longer found, removing: {ResourceId}", _resources[resourceKey].Resource.Id);
                _resources.Remove(resourceKey);
                removedResources++;
            }

            _logger.LogInformation(
                "Resource introspection completed. Total resources: {Count} (Added: {Added}, Removed: {Removed})",
                _resources.Count,
                addedResources,
                removedResources);
        }
    }
}
