namespace poolautoscaler.metrics.Dto
{
    /// <summary>VMSS SKU view for custom metric expressions.</summary>
    public class VmssSkuView
    {
        /// <summary>Desired capacity (node count).</summary>
        public long Capacity { get; set; }

        /// <summary>VM size name (e.g. Standard_D4s_v3).</summary>
        public string Name { get; set; }
    }
}
