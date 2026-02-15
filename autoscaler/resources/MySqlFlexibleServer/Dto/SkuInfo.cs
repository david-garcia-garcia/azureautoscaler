namespace poolautoscaler.resources.MySqlFlexibleServer.Dto
{
    /// <summary>SKU information including tier, name, and IOPS range.</summary>
    public class SkuInfo
    {
        /// <summary>Gets or sets the tier.</summary>
        public string Tier { get; set; }

        /// <summary>Gets or sets the SKU name.</summary>
        public string Sku { get; set; }

        /// <summary>Gets or sets the minimum IOPS.</summary>
        public int MinIops { get; set; }

        /// <summary>Gets or sets the maximum IOPS.</summary>
        public int MaxIops { get; set; }

        /// <summary>Initializes a new instance of the <see cref="SkuInfo"/> class.</summary>
        /// <param name="tier">The tier.</param>
        /// <param name="sku">The SKU name.</param>
        /// <param name="minIops">The minimum IOPS.</param>
        /// <param name="maxIops">The maximum IOPS.</param>
        public SkuInfo(string tier, string sku, int minIops, int maxIops)
        {
            this.Tier = tier;
            this.Sku = sku;
            this.MinIops = minIops;
            this.MaxIops = maxIops;
        }
    }
}
