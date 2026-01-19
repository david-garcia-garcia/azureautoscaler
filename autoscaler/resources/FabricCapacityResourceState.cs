using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.Fabric;
using Azure.ResourceManager.Fabric.Models;
using Microsoft.Extensions.Logging;

namespace poolautoscaler.resources
{
    public class FabricCapacityResourceState : ResourceState
    {
        public class FabricCapacityState
        {
            public string? Sku { get; set; }
        }

        public override object ExistingStateRaw => this.ExistingFabricCapacityState;

        public override object RequestedStateRaw => this.RequestedFabricCapacityState;

        public FabricCapacityState RequestedFabricCapacityState { get; set; }
        public FabricCapacityState ExistingFabricCapacityState { get; set; }

        private FabricCapacityResource? CapacityResourceCasted { get => this.Resource as FabricCapacityResource; }

        public FabricCapacityResourceState(string id, ILogger logger, Resource resourceConfiguration) : base(id, logger, resourceConfiguration)
        {
            if (!ResourceStateFactory.FabricCapacity.IsMatch(id))
            {
                throw new ArgumentException("Invalid Fabric Capacity resource ID", nameof(id));
            }
        }

        protected override async Task InternalRefreshAsync(ArmClient client, TokenCredential credential, CancellationToken cancellationToken)
        {
            this.Resource = await client.GetFabricCapacityResource(new ResourceIdentifier(this.ResourceId)).GetAsync(cancellationToken);

            var capacityResource = this.CapacityResourceCasted;
            if (capacityResource == null)
            {
                throw new Exception("Failed to get Fabric Capacity resource.");
            }

            // Populate resource tags
            this.PopulateResourceTags(capacityResource.Data.Tags);

            // Extract SKU from resource data
            string? currentSku = capacityResource.Data.Sku?.Name;

            this.ExistingFabricCapacityState = new FabricCapacityState()
            {
                Sku = currentSku
            };

            this.RequestedFabricCapacityState = new FabricCapacityState();
        }

        public void SetSku(string sku)
        {
            if (this.RequestedFabricCapacityState.Sku == null)
            {
                this.RequestedFabricCapacityState.Sku = sku;
                return;
            }

            // For SKU comparison, we want to take the higher SKU
            if (FabricCapacityResourceStateHelper.CompareSku(sku, this.RequestedFabricCapacityState.Sku) > 0)
            {
                this.RequestedFabricCapacityState.Sku = sku;
            }
        }

        public override ResourcePatchOperation PreparePatch()
        {
            ResourcePatchOperation result = new ResourcePatchOperation();

            var patch = new FabricCapacityState();

            patch.Sku = this.RequestedFabricCapacityState.Sku ?? this.ExistingFabricCapacityState.Sku;

            bool hasChanges = patch.Sku != null && patch.Sku != this.ExistingFabricCapacityState.Sku;

            result.PatchData = patch;
            result.HasChanges = hasChanges;
            result.Disruptive = hasChanges; // SKU changes are disruptive

            return result;
        }

        public override async Task ApplyChanges(ResourcePatchOperation operation, CancellationToken cancellationToken)
        {
            if (!(operation.PatchData is FabricCapacityState internalPatch))
            {
                throw new Exception("Invalid patch type.");
            }

            var capacityResource = this.CapacityResourceCasted;
            if (capacityResource == null)
            {
                throw new Exception("FabricCapacityResource is null. Resource may not have been refreshed.");
            }

            // Get the current SKU to preserve the tier
            var currentSku = capacityResource.Data.Sku;
            if (currentSku == null)
            {
                throw new Exception("Current SKU is null. Cannot determine tier.");
            }

            // Create patch with updated SKU (preserve tier, update name)
            FabricCapacityPatch patch = new FabricCapacityPatch();
            patch.Sku = new FabricSku(internalPatch.Sku, currentSku.Tier);

            var result = await capacityResource.UpdateAsync(Azure.WaitUntil.Completed, patch, cancellationToken);
            this.ValidateArmResult(result);
        }
    }
}
