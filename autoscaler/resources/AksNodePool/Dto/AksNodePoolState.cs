namespace poolautoscaler.resources.AksNodePool.Dto
{
    /// <summary>
    /// Represents the AKS node pool scaling state.
    /// </summary>
    public class AksNodePoolState
    {
        /// <summary>
        /// Gets or sets the minimum node count.
        /// </summary>
        public int? MinNodeCount { get; set; }

        /// <summary>
        /// Gets or sets the maximum node count.
        /// </summary>
        public int? MaxNodeCount { get; set; }
    }
}
