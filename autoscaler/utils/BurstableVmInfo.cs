namespace poolautoscaler.utils
{
    /// <summary>Burstable (B-series) VM credit and baseline data.</summary>
    public static class BurstableVmInfo
    {
        private static Dictionary<string, int> burstableMaxCredits = new Dictionary<string, int>
        {
            { "Standard_B1ls", 72 },
            { "Standard_B1s", 144 },
            { "Standard_B1ms", 288 },
            { "Standard_B2s", 576 },
            { "Standard_B2ms", 864 },
            { "Standard_B4ms", 1296 },
            { "Standard_B8ms", 1994 },
            { "Standard_B12ms", 2908 },
            { "Standard_B16ms", 3888 },
            { "Standard_B20ms", 4867 }
        };

        private static Dictionary<string, int> burstableCreditsPerHour = new Dictionary<string, int>
        {
            { "Standard_B1ls", 3 },
            { "Standard_B1s", 6 },
            { "Standard_B1ms", 12 },
            { "Standard_B2s", 24 },
            { "Standard_B2ms", 36 },
            { "Standard_B4ms", 54 },
            { "Standard_B8ms", 81 },
            { "Standard_B12ms", 121 },
            { "Standard_B16ms", 162 },
            { "Standard_B20ms", 202 }
        };

        private static Dictionary<string, int> burstableInitialCredits = new Dictionary<string, int>
        {
            { "Standard_B1ls", 30 },
            { "Standard_B1s", 30 },
            { "Standard_B1ms", 30 },
            { "Standard_B2s", 60 },
            { "Standard_B2ms", 60 },
            { "Standard_B4ms", 120 },
            { "Standard_B8ms", 240 },
            { "Standard_B12ms", 360 },
            { "Standard_B16ms", 480 },
            { "Standard_B20ms", 600 }
        };

        private static Dictionary<string, double> burstableBaselineCpu = new Dictionary<string, double>
        {
            { "Standard_B1ls", 5.0 },
            { "Standard_B1s", 10.0 },
            { "Standard_B1ms", 20.0 },
            { "Standard_B2s", 20.0 },
            { "Standard_B2ms", 30.0 },
            { "Standard_B4ms", 22.5 },
            { "Standard_B8ms", 17.0 },
            { "Standard_B12ms", 17.0 },
            { "Standard_B16ms", 17.0 },
            { "Standard_B20ms", 17.0 }
        };

        /// <summary>Returns baseline CPU % for the B-series SKU.</summary>
        /// <param name="sku">The B-series SKU name.</param>
        /// <returns>Baseline CPU percentage.</returns>
        public static double GetBurstableBaselinePerformance(string sku)
        {
            var key = (from p in burstableBaselineCpu.Keys
                       where p.Equals(sku, StringComparison.InvariantCultureIgnoreCase)
                       select p).First();

            return burstableBaselineCpu[key];
        }

        /// <summary>Returns initial credits for the B-series SKU.</summary>
        /// <param name="sku">The B-series SKU name.</param>
        /// <returns>Initial credits.</returns>
        public static int GetBurstableInitialCredits(string sku)
        {
            var key = (from p in burstableInitialCredits.Keys
                       where p.Equals(sku, StringComparison.InvariantCultureIgnoreCase)
                       select p).First();

            return burstableInitialCredits[key];
        }

        /// <summary>Returns credits earned per hour for the B-series SKU.</summary>
        /// <param name="sku">The B-series SKU name.</param>
        /// <returns>Credits per hour.</returns>
        public static int GetBurstableCreditsPerHour(string sku)
        {
            var key = (from p in burstableCreditsPerHour.Keys
                       where p.Equals(sku, StringComparison.InvariantCultureIgnoreCase)
                       select p).First();

            return burstableCreditsPerHour[key];
        }

        /// <summary>Returns max credits for the B-series SKU.</summary>
        /// <param name="sku">The B-series SKU name.</param>
        /// <returns>Max credits for the VM size.</returns>
        public static int GetMaxCreditsPerVmSize(string sku)
        {
            var key = (from p in burstableMaxCredits.Keys
                       where p.Equals(sku, StringComparison.InvariantCultureIgnoreCase)
                       select p).First();

            return burstableMaxCredits[key];
        }

        /// <summary>Returns true if the SKU is a B-series burstable VM.</summary>
        /// <param name="sku">The SKU name.</param>
        /// <returns>True if burstable B-series.</returns>
        public static bool IsBurstableSeries(string sku)
        {
            return (from p in burstableMaxCredits.Keys
                    where p.Equals(sku, StringComparison.InvariantCultureIgnoreCase)
                    select p).Any();
        }

    }
}
