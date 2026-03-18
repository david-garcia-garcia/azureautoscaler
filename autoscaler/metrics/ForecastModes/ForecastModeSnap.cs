using poolautoscaler.configuration;
using poolautoscaler.metrics.Dto;

namespace poolautoscaler.metrics.ForecastModes
{
    /// <summary>Snap mode: for each (day, slot), percentile over current slot plus next (numWindows-1) slots, wrapping across day/week.</summary>
    public sealed class ForecastModeSnap : IForecastModeStrategy
    {
        /// <inheritdoc />
        public string ModeName => "Snap";

        /// <inheritdoc />
        public bool TryApply(MetricForecastResult result, Metric metric)
        {
            var baseline = result.ValueByDayAndHour;
            if (baseline == null || !baseline.Any())
            {
                return false;
            }

            var numWindows = metric.ForecastSnapStepWindows ?? 3;
            if (numWindows < 1)
            {
                numWindows = 1;
            }

            var percentile = Math.Clamp(metric.ForecastSnapPercentile ?? 95, 0, 100);
            var slotsPerDay = (24 * 60) / result.SlotMinutes;

            result.SnappedValueByDayAndHour = ComputeStepSnap(baseline, slotsPerDay, numWindows, percentile);
            result.SnappedModeName = this.ModeName;
            result.SnappedPercentile = percentile;
            result.SnappedParameterSummary = $"{numWindows} windows";
            return true;
        }

        /// <summary>Compute snapped values: each (day, slot) gets percentile of current + next (numWindows-1) slots (wrap).</summary>
        /// <param name="baseline">Baseline forecast per (day, slot).</param>
        /// <param name="slotsPerDay">Number of slots per day.</param>
        /// <param name="numWindows">Number of consecutive slots (current + future) to consider.</param>
        /// <param name="percentile">Percentile 0-100.</param>
        /// <returns>Snapped forecast per (day, slot).</returns>
        internal static Dictionary<DayOfWeek, Dictionary<int, double>> ComputeStepSnap(
            Dictionary<DayOfWeek, Dictionary<int, double>> baseline,
            int slotsPerDay,
            int numWindows,
            int percentile)
        {
            var days = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday };
            var totalSlots = 7 * slotsPerDay;

            int LinearIndex(DayOfWeek d, int slot)
            {
                var dayIdx = d == DayOfWeek.Sunday ? 6 : (int)d - 1;
                return (dayIdx * slotsPerDay) + slot;
            }

            (DayOfWeek Day, int Slot) LinearToDaySlot(int linearIdx)
            {
                var normalized = ((linearIdx % totalSlots) + totalSlots) % totalSlots;
                var dayIdx = normalized / slotsPerDay;
                var slot = normalized % slotsPerDay;
                return (days[dayIdx], slot);
            }

            var snapped = new Dictionary<DayOfWeek, Dictionary<int, double>>();
            foreach (var day in days)
            {
                if (!baseline.TryGetValue(day, out var bySlot))
                {
                    continue;
                }

                var outBySlot = new Dictionary<int, double>();
                foreach (var slotIndex in bySlot.Keys)
                {
                    var linearStart = LinearIndex(day, slotIndex);
                    var values = new List<double>();
                    for (var w = 0; w < numWindows; w++)
                    {
                        var (d, s) = LinearToDaySlot(linearStart + w);
                        if (baseline.TryGetValue(d, out var slotDict) && slotDict.TryGetValue(s, out var v))
                        {
                            values.Add(v);
                        }
                    }

                    if (values.Any())
                    {
                        outBySlot[slotIndex] = ForecastModeHelper.PercentileValue(values, percentile);
                    }
                }

                if (outBySlot.Any())
                {
                    snapped[day] = outBySlot;
                }
            }

            return snapped;
        }
    }
}
