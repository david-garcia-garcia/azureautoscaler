using System.Text.RegularExpressions;

namespace poolautoscaler.metrics.Dto
{
    /// <summary>
    /// Fallback VM size helpers when <see cref="IVmSizeResolver"/> is not available (e.g. no location).
    /// Memory is not available without the API; cores are inferred from SKU name (e.g. Standard_D4s_v3 -> 4).
    /// </summary>
    public static class VmSizeInfo
    {
        /// <summary>Gets memory in bytes for a VM size. Returns 0 when not using cache (no API data).</summary>
        /// <param name="vmSize">The VM SKU name.</param>
        /// <returns>0 (use <see cref="IVmSizeResolver"/> + <see cref="CustomMetricHelpers"/> with resolver for real values).</returns>
        public static long GetMemoryBytes(string vmSize)
        {
            return 0;
        }

        /// <summary>Extracts core count from VM size name (e.g. Standard_D4s_v3 -> 4).</summary>
        /// <param name="vmSize">The VM SKU name.</param>
        /// <returns>Core count, or 0 if unknown.</returns>
        public static int GetCoreCount(string vmSize)
        {
            if (string.IsNullOrEmpty(vmSize))
            {
                return 0;
            }

            var match = Regex.Match(vmSize, @"[A-Z](\d+)");
            return match.Success ? int.Parse(match.Groups[1].Value) : 0;
        }
    }
}
