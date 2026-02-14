using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using poolautoscaler.configuration;

namespace poolautoscaler.resourcemanagement
{
    /// <summary>
    /// Discovers and stores resource states. Runs expansion from configuration and keeps
    /// the current set of resources; call <see cref="DiscoverAsync"/> periodically to refresh.
    /// </summary>
    public class ResourceManager
    {
        private readonly Dictionary<string, ResourceState> resources = new Dictionary<string, ResourceState>();
        private readonly ILogger logger;
        private readonly ILoggerFactory logFactory;
        private DateTime lastDiscovery = DateTime.MinValue;

        public ResourceManager(ILoggerFactory logFactory)
        {
            this.logFactory = logFactory;
            this.logger = logFactory.CreateLogger("ResourceManager");
        }

        /// <summary>
        /// Current set of discovered resources (key: resource id, value: state). Do not modify the dictionary.
        /// </summary>
        public IReadOnlyDictionary<string, ResourceState> Resources => new ReadOnlyDictionary<string, ResourceState>(this.resources);

        /// <summary>
        /// Runs resource discovery if <paramref name="discoveryFrequency"/> has elapsed since the last run.
        /// Call every loop iteration; discovery is performed only when due. First call always runs.
        /// </summary>
        /// <param name="client">ARM client for expansion calls.</param>
        /// <param name="resourceConfigs">Resource configuration (e.g. Configuration.Resources).</param>
        /// <param name="discoveryFrequency">Minimum interval between discovery runs.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task that completes when discovery has run (or was skipped).</returns>
        public async Task DiscoverAsync(
            Azure.ResourceManager.ArmClient client,
            IEnumerable<Resource> resourceConfigs,
            TimeSpan discoveryFrequency,
            CancellationToken cancellationToken = default)
        {
            if ((DateTime.UtcNow - this.lastDiscovery) < discoveryFrequency)
            {
                return;
            }

            try
            {
                await this.DiscoverCoreAsync(client, resourceConfigs, cancellationToken);
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Failed to discover resources: {Message}", ex.Message);
            }
            finally
            {
                this.lastDiscovery = DateTime.UtcNow;
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
            this.logger.LogInformation("Performing resource discovery and expansion...");

            var discoveredResources = new HashSet<string>();
            int addedResources = 0;
            int removedResources = 0;

            if (resourceConfigs == null)
            {
                this.logger.LogTrace("No resource configuration to discover.");
                return;
            }

            // Phase 1: Discover all resources that should exist and add new ones
            foreach (var resource in resourceConfigs)
            {
                if (resource.Enabled == false)
                {
                    this.logger.LogTrace(
                        "Skipping disabled resource: {Ids}",
                        resource.Resources != null ? string.Join(",", resource.Resources.Values.Select(i => i.Id)) : "");
                    continue;
                }

                if (resource.Resources == null)
                {
                    continue;
                }

                foreach (var resourceInstance in resource.Resources)
                {
                    Dictionary<string, string> expandedResourceIds;
                    try
                    {
                        expandedResourceIds = await ResourceStateFactory.ExpandResources(
                            client,
                            resourceInstance.Key,
                            resourceInstance.Value.ResourceId,
                            this.logger,
                            cancellationToken);
                    }
                    catch (Azure.RequestFailedException ex) when (ex.Status == 404)
                    {
                        this.logger.LogError(
                            "Skipping deleted or non existing resource during expansion: {ResourceId}. Please remove this resource from your configuration.",
                            resourceInstance.Value.ResourceId);
                        continue;
                    }
                    catch (Azure.RequestFailedException ex) when (ex.Status == 403)
                    {
                        this.logger.LogError(
                            ex,
                            "Not authorized to access resource during expansion: {ResourceId}. Please check your credentials and permissions.",
                            resourceInstance.Value.ResourceId);
                        continue;
                    }

                    foreach (var expandedResourceId in expandedResourceIds)
                    {
                        resourceInstance.Value.Id = resourceInstance.Key;
                        discoveredResources.Add(expandedResourceId.Key);

                        if (this.resources.ContainsKey(expandedResourceId.Key))
                        {
                            this.logger.LogTrace("Keeping existing resource: {Id}", expandedResourceId.Value);
                        }
                        else
                        {
                            this.logger.LogInformation("Adding new resource {Key}: {Id}", expandedResourceId.Key, expandedResourceId.Value);

                            var resourceLogger = this.logFactory.CreateLogger(expandedResourceId.Key);
                            var state = ResourceStateFactory.Create(
                                expandedResourceId.Value,
                                resourceLogger,
                                resource,
                                resourceInstance.Value);
                            resourceLogger.LogDebug(
                                "Replacements: {Replacements}",
                                string.Join(", ", state.ResourceParts.Select((i) => $"{i.Key}={i.Value}")));
                            this.resources[expandedResourceId.Key] = state;
                            addedResources++;
                        }
                    }
                }
            }

            // Phase 2: Cleanup - remove resources that no longer exist
            var resourcesToRemove = new List<string>();
            foreach (var existingResourceKey in this.resources.Keys)
            {
                if (!discoveredResources.Contains(existingResourceKey))
                {
                    resourcesToRemove.Add(existingResourceKey);
                }
            }

            foreach (var resourceKey in resourcesToRemove)
            {
                this.logger.LogInformation("Resource no longer found, removing: {ResourceId}", this.resources[resourceKey].Resource.Id);
                this.resources.Remove(resourceKey);
                removedResources++;
            }

            this.logger.LogInformation(
                "Resource introspection completed. Total resources: {Count} (Added: {Added}, Removed: {Removed})",
                this.resources.Count,
                addedResources,
                removedResources);
        }
    }
}
