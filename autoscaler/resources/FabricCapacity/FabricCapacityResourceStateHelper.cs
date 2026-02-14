namespace poolautoscaler.resources.FabricCapacity
{
    public static class FabricCapacityResourceStateHelper
    {
        public static readonly string[] ValidSkus = new string[]
        {
            "F2", "F4", "F8", "F16", "F32", "F64", "F128", "F256", "F512", "F1024", "F2048"
        };

        public static int CompareSku(string sku1, string sku2)
        {
            var index1 = Array.IndexOf(ValidSkus, sku1);
            var index2 = Array.IndexOf(ValidSkus, sku2);

            if (index1 == -1 || index2 == -1)
            {
                throw new ArgumentException($"Invalid SKU values. SKU1: '{sku1}', SKU2: '{sku2}'");
            }

            return index1.CompareTo(index2);
        }

        public static string[] GetCapacityValues()
        {
            return ValidSkus;
        }

        public static bool IsValidSku(string sku)
        {
            return Array.IndexOf(ValidSkus, sku) >= 0;
        }
    }
}
