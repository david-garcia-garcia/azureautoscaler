namespace poolautoscaler.resources.MySqlFlexibleServer.Dto
{
    public class SkuInfo
    {
        public string Tier { get; set; }

        public string Sku { get; set; }

        public int MinIops { get; set; }

        public int MaxIops { get; set; }

        public SkuInfo(string tier, string sku, int minIops, int maxIops)
        {
            this.Tier = tier;
            this.Sku = sku;
            this.MinIops = minIops;
            this.MaxIops = maxIops;
        }
    }
}
