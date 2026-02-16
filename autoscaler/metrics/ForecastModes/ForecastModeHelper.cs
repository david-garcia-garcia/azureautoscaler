namespace poolautoscaler.metrics.ForecastModes
{
    /// <summary>Shared helpers for forecast mode strategies (e.g. percentile).</summary>
    public static class ForecastModeHelper
    {
        /// <summary>Minutes per day (UTC day length).</summary>
        public const int MinutesPerDay = 1440;

        /// <summary>Percentile (0-100) of values; returns 0 if empty.</summary>
        /// <param name="values">List of values (will be sorted).</param>
        /// <param name="percentile">Percentile 0-100.</param>
        /// <returns>The value at the requested percentile, or 0 if empty.</returns>
        public static double PercentileValue(List<double> values, int percentile)
        {
            if (values == null || values.Count == 0)
            {
                return 0;
            }

            if (values.Count == 1)
            {
                return values[0];
            }

            var sorted = values.OrderBy(x => x).ToList();
            var idx = (int)Math.Round(((sorted.Count - 1) * percentile) / 100.0, MidpointRounding.AwayFromZero);
            idx = Math.Clamp(idx, 0, sorted.Count - 1);
            return sorted[idx];
        }
    }
}
