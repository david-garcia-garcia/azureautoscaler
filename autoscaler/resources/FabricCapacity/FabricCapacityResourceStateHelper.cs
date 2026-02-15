namespace poolautoscaler.resources.FabricCapacity
{
    /// <summary>
    /// Helper for Fabric capacity SKU comparison and validation.
    /// </summary>
    public static class FabricCapacityResourceStateHelper
    {
        /// <summary>
        /// Valid Fabric capacity SKU names (smallest to largest).
        /// </summary>
        public static readonly string[] ValidSkus = new string[]
        {
            "F2", "F4", "F8", "F16", "F32", "F64", "F128", "F256", "F512", "F1024", "F2048"
        };

        /// <summary>
        /// Compares two Fabric capacity SKUs by order (smallest to largest).
        /// </summary>
        /// <param name="sku1">First SKU name.</param>
        /// <param name="sku2">Second SKU name.</param>
        /// <returns>Negative if sku1 &lt; sku2, 0 if equal, positive if sku1 &gt; sku2.</returns>
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
        /// Gets the list of valid Fabric capacity SKU values.
        /// </summary>
        /// <returns>Array of valid SKU names.</returns>
        public static string[] GetCapacityValues()
        {
            return ValidSkus;
        }

        /// <summary>
        /// Determines whether the given SKU is a valid Fabric capacity SKU.
        /// </summary>
        /// <param name="sku">The SKU name to validate.</param>
        /// <returns>True if valid; otherwise false.</returns>
        public static bool IsValidSku(string sku)
        {
            return Array.IndexOf(ValidSkus, sku) >= 0;
        }
    }
}
