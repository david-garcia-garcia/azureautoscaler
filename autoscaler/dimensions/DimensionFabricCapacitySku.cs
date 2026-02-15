using Azure.Core;
using Azure.ResourceManager;
using Microsoft.Extensions.Logging;
using poolautoscaler.configuration;
using poolautoscaler.resourcemanagement;
using poolautoscaler.resources.FabricCapacity;

namespace poolautoscaler.dimensions
{
    /// <summary>Fabric capacity SKU dimension.</summary>
    internal class DimensionFabricCapacitySku : IDimension
    {
        /// <inheritdoc/>
        public bool CanApplyDimension(ResourceState resource, ScalingRule rule, ILogger logger)
        {
            if (resource is FabricCapacityResourceState
                && rule.Dimension == "Sku")
            {
                return true;
            }

            return false;
        }

        /// <inheritdoc/>
        public void ValidateRuleConfiguration(ScalingRule rule)
        {
            if (!string.IsNullOrEmpty(rule.DimensionValueMin) && !FabricCapacityResourceStateHelper.IsValidSku(rule.DimensionValueMin))
            {
                throw new ArgumentException($"Invalid DimensionValueMin: '{rule.DimensionValueMin}'. Valid values are: {string.Join(", ", FabricCapacityResourceStateHelper.ValidSkus)}");
            }

            if (!string.IsNullOrEmpty(rule.DimensionValueMax) && !FabricCapacityResourceStateHelper.IsValidSku(rule.DimensionValueMax))
            {
                throw new ArgumentException($"Invalid DimensionValueMax: '{rule.DimensionValueMax}'. Valid values are: {string.Join(", ", FabricCapacityResourceStateHelper.ValidSkus)}");
            }
        }

        /// <inheritdoc/>
        public int Compare(ArmResource resource, string dimensionValue1, string dimensionValue2)
        {
            // For Fabric capacities, we can use the helper method
            // The resource type check is handled in CanApplyDimension
            return FabricCapacityResourceStateHelper.CompareSku(dimensionValue1, dimensionValue2);
        }

        /// <inheritdoc/>
        public string GetCurrentDimensionValue(ResourceState resource)
        {
            if (!(resource is FabricCapacityResourceState fabricCapacityState))
            {
                throw new ArgumentException($"Resource is not {nameof(FabricCapacityResourceState)}.");
            }

            return fabricCapacityState.ExistingFabricCapacityState.Sku ?? "F2";
        }

        /// <inheritdoc/>
        public string GetNextDimensionValue(ResourceState resource, string value)
        {
            var capacityValues = FabricCapacityResourceStateHelper.GetCapacityValues();
            int position = Array.IndexOf(capacityValues, value);

            if (position < 0)
            {
                throw new ArgumentException($"Invalid SKU value: '{value}'");
            }

            if (position < capacityValues.Length - 1)
            {
                return capacityValues[position + 1];
            }

            // Already maxed out
            return value;
        }

        /// <inheritdoc/>
        public string GetPreviousDimensionValue(ResourceState resource, string value)
        {
            var capacityValues = FabricCapacityResourceStateHelper.GetCapacityValues();
            int position = Array.IndexOf(capacityValues, value);

            if (position < 0)
            {
                throw new ArgumentException($"Invalid SKU value: '{value}'");
            }

            if (position == 0)
            {
                // If current one is the lowest, then this is the target.
                return value;
            }

            return capacityValues[position - 1];
        }

        /// <inheritdoc/>
        public string? GetRequestedDimensionValue(ResourceState resource)
        {
            if (!(resource is FabricCapacityResourceState fabricCapacityState))
            {
                throw new ArgumentException($"Resource is not {nameof(FabricCapacityResourceState)}.");
            }

            return fabricCapacityState.RequestedFabricCapacityState?.Sku;
        }

        /// <inheritdoc/>
        public async Task SetDimensionValue(
            CancellationToken stoppingToken,
            ResourceState resource,
            ILogger logger,
            TokenCredential credential,
            string value)
        {
            if (!(resource is FabricCapacityResourceState fabricCapacityState))
            {
                throw new ArgumentException($"Resource is not {nameof(FabricCapacityResourceState)}.");
            }

            if (!FabricCapacityResourceStateHelper.IsValidSku(value))
            {
                throw new ArgumentException($"Invalid SKU value: '{value}'. Valid values are: {string.Join(", ", FabricCapacityResourceStateHelper.ValidSkus)}");
            }

            fabricCapacityState.SetSku(value);
        }
    }
}
