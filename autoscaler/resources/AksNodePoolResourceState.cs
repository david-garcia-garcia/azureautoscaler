using System.Text.RegularExpressions;
using Azure;
using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.Compute;
using Azure.ResourceManager.ContainerService;
using Azure.ResourceManager.Resources;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace poolautoscaler.resources
{
    public class AksNodePoolResourceState : ResourceState
    {
        public class AksNodePoolState
        {
            public int? MinNodeCount { get; set; }
            public int? MaxNodeCount { get; set; }
        }

        public override object ExistingStateRaw => this.ExistingAksNodePoolState;

        public override object RequestedStateRaw => this.RequestedAksNodePoolState;

        public AksNodePoolState RequestedAksNodePoolState { get; set; }

        public AksNodePoolState ExistingAksNodePoolState { get; set; }

        public AksNodePoolResourceState(string id, ILogger logger, Resource resourceConfiguration) : base(id, logger, resourceConfiguration)
        {
            if (!ResourceStateFactory.AksNodePool.IsMatch(id))
            {
                throw new ArgumentException("Invalid AKS Node Pool resource ID", nameof(id));
            }
        }

        protected async Task<string> GetVmssIdForNodePool(ArmClient client, TokenCredential credential,
            CancellationToken cancellationToken, ContainerServiceAgentPoolResource nodePool)
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

            // Get the cluster info.... only to be able to figure the VMSS bound to a node pool, because
            // the metrics available for the nodepool... do not exist!"
            // https://github.com/Azure/AKS/issues/5001
            var clusterId = this.ReplaceResourceParts(
                "/subscriptions/${subscriptionId}/resourceGroups/${resourceGroupName}/providers/Microsoft.ContainerService/managedClusters/${clusterName}");

            var clusterinfo = (ContainerServiceManagedClusterResource)await client.GetContainerServiceManagedClusterResource(
                new ResourceIdentifier(clusterId)).GetAsync(cancellationToken);

            SubscriptionCollection subscriptions = client.GetSubscriptions();

            SubscriptionResource subscription = await subscriptions.GetAsync(this.ReplaceResourceParts("${subscriptionId}"), cancellationToken);

            ResourceGroupCollection resourceGroups = subscription.GetResourceGroups();

            ResourceGroupResource resourceGroup = await resourceGroups.GetAsync(clusterinfo.Data.NodeResourceGroup, cancellationToken);

            VirtualMachineScaleSetCollection scaleSets = resourceGroup.GetVirtualMachineScaleSets();

            // Finally we get the resource itself
            // Note: for this last step in this example, Azure.ResourceManager.Compute is needed
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

        protected override async Task InternalRefreshAsync(ArmClient client, TokenCredential credential, CancellationToken cancellationToken)
        {
            this.Resource = await client.GetContainerServiceAgentPoolResource(new ResourceIdentifier(this.ResourceId)).GetAsync(cancellationToken);

            var nodePool = (ContainerServiceAgentPoolResource)this.Resource;

            if (nodePool.Data.ProvisioningState == "Failed")
            {
                this.Logger.LogWarning($"AKS node pool in provisioning state '{nodePool.Data.ProvisioningState}'. Resource will be disabled until next refresh.");
                this.DisabledUntil = DateTime.MaxValue;
                return;
            }

            this.DisabledUntil = null;

            this.ResourceParts["virtualMachineScaleSetId"] = await this.GetVmssIdForNodePool(client, credential, cancellationToken, nodePool);

            this.RequestedAksNodePoolState = new AksNodePoolState();

            this.ExistingAksNodePoolState = new AksNodePoolState()
            {
                MaxNodeCount = nodePool.Data.MaxCount,
                MinNodeCount = nodePool.Data.MinCount
            };
        }

        protected override string GetResourceIdForChangeHistory()
        {
            if (this.IsDisabled())
            {
                // Null means do not load any history
                return null;
            }

            return this.ResourceParts["virtualMachineScaleSetId"];
        }

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

        /// <summary>
        /// /
        /// </summary>
        /// <returns></returns>
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

        /// <summary>
        /// 
        /// </summary>
        /// <param name="operation"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
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
    }
}