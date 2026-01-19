namespace poolautoscaler.resources
{
    public static class FabricCapacityResourceStateHelper
    {
        // Valid Fabric capacity SKUs in order from smallest to largest
        public static readonly string[] ValidSkus = new string[]
        {
            "F2", "F4", "F8", "F16", "F32", "F64", "F128", "F256", "F512", "F1024", "F2048"
        };

        /// <summary>
        /// Compares two Fabric SKU values
        /// Returns: -1 if sku1 < sku2, 0 if equal, 1 if sku1 > sku2
        /// </summary>
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

        /// <summary>
        /// Gets all valid SKU values
        /// </summary>
        public static string[] GetCapacityValues()
        {
            return ValidSkus;
        }

        /// <summary>
        /// Validates if a SKU is valid
        /// </summary>
        public static bool IsValidSku(string sku)
        {
            return Array.IndexOf(ValidSkus, sku) >= 0;
        }
    }
}
