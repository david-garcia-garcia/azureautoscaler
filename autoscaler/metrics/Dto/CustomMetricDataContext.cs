namespace poolautoscaler.metrics.Dto
{
    /// <summary>
    /// Context passed to custom metric DataExpression.
    /// Provides Resource (the resource being scaled), ExistingState, ResourceParts, Helpers, and Extra for resource-type-specific data.
    /// </summary>
    public class CustomMetricDataContext
    {
        /// <summary>
        /// The resource being scaled (e.g. node pool, MySQL server). ARM resource or state DTO.
        /// For resource-specific related data (e.g. VMSS for AKS node pool), use <see cref="Extra"/>.
        /// </summary>
        public object Resource { get; set; }

        /// <summary>Existing state DTO (AksNodePoolState, MySqlFlexibleServerState, etc.).</summary>
        public object ExistingState { get; set; }

        /// <summary>Resource ID parts (subscriptionId, resourceGroupName, virtualMachineScaleSetId, etc.).</summary>
        public Dictionary<string, string> ResourceParts { get; set; } = new Dictionary<string, string>();

        /// <summary>Helper functions (e.g. VmSizeToMemory, VmSizeToCores).</summary>
        public CustomMetricHelpers Helpers { get; set; }

        /// <summary>
        /// Extra, resource-type-specific data. For AKS node pool: key "Vmss" gives a <see cref="VmssMetricView"/> (Sku.Capacity, Sku.Name).
        /// Use in expressions e.g. data.Extra["Vmss"].Sku.Capacity.
        /// </summary>
        public IDictionary<string, object> Extra { get; set; } = new Dictionary<string, object>();
    }
}
