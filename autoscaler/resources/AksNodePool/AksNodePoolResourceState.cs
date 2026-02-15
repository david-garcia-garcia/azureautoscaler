using System.Text.RegularExpressions;
using Azure;
using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.Compute;
using Azure.ResourceManager.ContainerService;
using Azure.ResourceManager.Resources;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using poolautoscaler.configuration;
using poolautoscaler.resourcemanagement;
using poolautoscaler.resourcemanagement.Dto;
using poolautoscaler.resources.AksNodePool.Dto;
using poolautoscaler.utils;

namespace poolautoscaler.resources.AksNodePool
{
    /// <summary>
    /// Resource state for an AKS node pool.
    /// </summary>
    public class AksNodePoolResourceState : ResourceState
    {
        /// <inheritdoc />
        public override object ExistingStateRaw => this.ExistingAksNodePoolState;

        /// <inheritdoc />
        public override object RequestedStateRaw => this.RequestedAksNodePoolState;

        /// <summary>
        /// Gets or sets the requested node pool state (min/max count).
        /// </summary>
        public AksNodePoolState RequestedAksNodePoolState { get; set; }

        /// <summary>
        /// Gets or sets the current existing node pool state from Azure.
        /// </summary>
        public AksNodePoolState ExistingAksNodePoolState { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="AksNodePoolResourceState"/> class.
        /// </summary>
        /// <param name="id">The AKS node pool resource ID.</param>
        /// <param name="logger">The logger.</param>
        /// <param name="resourceConfiguration">The resource configuration.</param>
        public AksNodePoolResourceState(string id, ILogger logger, Resource resourceConfiguration) : base(id, logger, resourceConfiguration)
        {
            if (!ResourceStateFactory.AksNodePool.IsMatch(id))
            {
                throw new ArgumentException("Invalid AKS Node Pool resource ID", nameof(id));
            }
        }

        /// <summary>
        /// Sets the requested minimum node count (only increases if already set).
        /// </summary>
        /// <param name="minNodeCount">The minimum node count.</param>
        public void SetMinNodeCount(int minNodeCount)
        {
            if (this.RequestedAksNodePoolState.MinNodeCount == null)
            {
                this.RequestedAksNodePoolState.MinNodeCount = minNodeCount;
                return;
            }

            if (minNodeCount > this.RequestedAksNodePoolState.MinNodeCount)
            {
                this.RequestedAksNodePoolState.MinNodeCount = minNodeCount;
            }
        }

        /// <inheritdoc />
        public override ResourcePatchOperation PreparePatch()
        {
            ResourcePatchOperation result = new ResourcePatchOperation();

            var patch = new AksNodePoolState();

            if (this.RequestedAksNodePoolState.MinNodeCount != null)
            {
                patch.MinNodeCount = this.RequestedAksNodePoolState.MinNodeCount;
            }

            patch.MaxNodeCount = this.ExistingAksNodePoolState.MaxNodeCount;

            if (patch.MinNodeCount > this.ExistingAksNodePoolState.MaxNodeCount)
            {
                patch.MaxNodeCount = patch.MinNodeCount;
            }

            result.HasChanges = (patch.MinNodeCount.HasValue && patch.MinNodeCount != this.ExistingAksNodePoolState.MinNodeCount)
                || (patch.MaxNodeCount.HasValue && patch.MaxNodeCount != this.ExistingAksNodePoolState.MaxNodeCount);

            result.PatchData = patch;
            result.Disruptive = false;

            return result;
        }

        /// <inheritdoc />
        public override async Task ApplyChanges(ResourcePatchOperation operation, CancellationToken cancellationToken)
        {
            var nodePool = (ContainerServiceAgentPoolResource)this.Resource;

            if (!(operation.PatchData is AksNodePoolState internalPatch))
            {
                throw new Exception("Invalid patch type.");
            }

            ContainerServiceAgentPoolData patch = new ContainerServiceAgentPoolData();

            patch.MaxCount = internalPatch.MaxNodeCount;
            patch.MinCount = internalPatch.MinNodeCount;

            var result = await nodePool.UpdateAsync(Azure.WaitUntil.Completed, patch, cancellationToken);
            this.ValidateArmResult(result);
        }

        /// <summary>
        /// Gets the Virtual Machine Scale Set ID for the node pool (cached when possible).
        /// </summary>
        /// <param name="client">The ARM client.</param>
        /// <param name="credential">The token credential.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <param name="nodePool">The agent pool resource.</param>
        /// <returns>The VMSS resource ID.</returns>
        protected async Task<string> GetVmssIdForNodePool(
            ArmClient client,
            TokenCredential credential,
            CancellationToken cancellationToken,
            ContainerServiceAgentPoolResource nodePool)
        {
            var cacheKey = "vmss-for-pool-" + nodePool.Data.Name;

            if (this.Cache.TryGetValue(cacheKey, out var cacheItem))
            {
                string vmssId = (string)cacheItem;

                try
                {
                    var scaleSet = await client.GetVirtualMachineScaleSetResource(new ResourceIdentifier(vmssId)).GetAsync(cancellationToken: cancellationToken);
                    return (string)cacheItem;
                }
                catch (RequestFailedException requestFailedException) when (requestFailedException.Status == 404)
                {
                    this.Logger.LogInformation($"Cached Virtual Machine Scale Set ID {vmssId} for pool is no longer valid.");
                }
            }

            var clusterId = this.ReplaceResourceParts(
                "/subscriptions/${subscriptionId}/resourceGroups/${resourceGroupName}/providers/Microsoft.ContainerService/managedClusters/${clusterName}");

            var clusterinfo = (ContainerServiceManagedClusterResource)await client.GetContainerServiceManagedClusterResource(
                new ResourceIdentifier(clusterId)).GetAsync(cancellationToken);

            SubscriptionCollection subscriptions = client.GetSubscriptions();

            SubscriptionResource subscription = await subscriptions.GetAsync(this.ReplaceResourceParts("${subscriptionId}"), cancellationToken);

            ResourceGroupCollection resourceGroups = subscription.GetResourceGroups();

            ResourceGroupResource resourceGroup = await resourceGroups.GetAsync(clusterinfo.Data.NodeResourceGroup, cancellationToken);

            VirtualMachineScaleSetCollection scaleSets = resourceGroup.GetVirtualMachineScaleSets();

            await foreach (var scaleSet in scaleSets.GetAllAsync(cancellationToken))
            {
                if (scaleSet.Data.Sku.Name == nodePool.Data.VmSize
                    && Regex.IsMatch(scaleSet.Data.Name, $"^aks-{nodePool.Data.Name}-|^aks{nodePool.Data.Name}$"))
                {
                    this.Cache.Set(cacheKey, scaleSet.Id.ToString(), DateTimeOffset.UtcNow.AddHours(48));
                    return scaleSet.Id.ToString();
                }
            }

            throw new Exception("Could not find Virtual Machine Scale Set for Node Pool.");
        }

        /// <inheritdoc />
        protected override async Task InternalRefreshAsync(ArmClient client, TokenCredential credential, CancellationToken cancellationToken)
        {
            this.Resource = await client.GetContainerServiceAgentPoolResource(new ResourceIdentifier(this.ResourceId)).GetAsync(cancellationToken);

            var nodePool = (ContainerServiceAgentPoolResource)this.Resource;

            if (nodePool.Data.ProvisioningState != "Succeeded")
            {
                this.Logger.LogWarning($"AKS node pool in provisioning state '{nodePool.Data.ProvisioningState}'. Resource will be disabled until next refresh.");
                this.DisabledUntil["ProvisioningState"] = DateTime.MaxValue;
                return;
            }

            this.DisabledUntil.TryRemove("ProvisioningState");

            this.ResourceParts["virtualMachineScaleSetId"] = await this.GetVmssIdForNodePool(client, credential, cancellationToken, nodePool);

            this.PopulateResourceTags(nodePool.Data.Tags);

            if (nodePool.Data.NodeLabels != null)
            {
                foreach (var label in nodePool.Data.NodeLabels)
                {
                    this.ResourceTags[label.Key] = label.Value;
                }
            }

            this.RequestedAksNodePoolState = new AksNodePoolState();

            this.ExistingAksNodePoolState = new AksNodePoolState()
            {
                MaxNodeCount = nodePool.Data.MaxCount,
                MinNodeCount = nodePool.Data.MinCount
            };
        }

        /// <inheritdoc />
        protected override string GetResourceIdForChangeHistory()
        {
            if (this.IsDisabled())
            {
                return null;
            }

            return this.ResourceParts["virtualMachineScaleSetId"];
        }
    }
}
