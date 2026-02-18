namespace poolautoscaler.metrics.Dto
{
    /// <summary>View of VMSS data for custom metric expressions (Sku.Capacity, Sku.Name).</summary>
    public class VmssMetricView
    {
        /// <summary>VMSS SKU view.</summary>
        public VmssSkuView Sku { get; set; } = new VmssSkuView();
    }
}
