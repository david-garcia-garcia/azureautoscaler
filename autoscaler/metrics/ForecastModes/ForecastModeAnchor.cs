using System.Globalization;
using poolautoscaler.configuration;
using poolautoscaler.metrics.Dto;

namespace poolautoscaler.metrics.ForecastModes
{
    /// <summary>Anchors mode: fixed interval boundaries (e.g. 05:00, 20:00); each interval gets the percentile of baseline in that interval.</summary>
    public sealed class ForecastModeAnchor : IForecastModeStrategy
    {
        /// <inheritdoc />
        public string ModeName => "Anchors";

        /// <inheritdoc />
        public bool TryApply(MetricForecastResult result, Metric metric)
        {
            var baseline = result.ValueByDayAndHour;
            if (baseline == null || !baseline.Any())
            {
                return false;
            }

            var anchorMinutes = ParseAnchorHoursToMinutes(metric.ForecastSnapAnchorHours);
            if (anchorMinutes == null || anchorMinutes.Count == 0)
            {
                return false;
            }

            var percentile = Math.Clamp(metric.ForecastSnapPercentile ?? 95, 0, 100);
            var slotMinutes = result.SlotMinutes;
            var slotsPerDay = (24 * 60) / slotMinutes;

            result.SnappedValueByDayAndHour = ComputeAnchorSnap(baseline, slotMinutes, slotsPerDay, anchorMinutes, percentile);
            result.SnappedModeName = this.ModeName;
            result.SnappedPercentile = percentile;
            result.SnappedParameterSummary = FormatAnchorHoursSummary(anchorMinutes);
            return true;
        }

        /// <summary>Parse list of "HH:mm" or "H:mm" to minutes from midnight. Returns sorted list or null if none valid.</summary>
        /// <param name="anchorHours">List of time strings (e.g. "05:00", "20:00").</param>
        /// <returns>Sorted list of minutes from midnight, or null if none valid.</returns>
        internal static List<int>? ParseAnchorHoursToMinutes(List<string>? anchorHours)
        {
            if (anchorHours == null || anchorHours.Count == 0)
            {
                return null;
            }

            var list = new List<int>();
            foreach (var s in anchorHours)
            {
                if (string.IsNullOrWhiteSpace(s))
                {
                    continue;
                }

                if (TimeSpan.TryParse(s.Trim(), CultureInfo.InvariantCulture, out var ts))
                {
                    var minutes = (int)Math.Round(ts.TotalMinutes) % ForecastModeHelper.MinutesPerDay;
                    if (minutes < 0)
                    {
                        minutes += ForecastModeHelper.MinutesPerDay;
                    }

                    list.Add(minutes);
                }
            }

            if (list.Count == 0)
            {
                return null;
            }

            list.Sort();
            return list;
        }

        /// <summary>Format anchor minutes as "HH:mm, HH:mm" for logging.</summary>
        /// <param name="anchorMinutes">Minutes from midnight.</param>
        /// <returns>Comma-separated "HH:mm" string.</returns>
        internal static string FormatAnchorHoursSummary(List<int> anchorMinutes)
        {
            if (anchorMinutes == null || anchorMinutes.Count == 0)
            {
                return string.Empty;
            }

            return string.Join(", ", anchorMinutes.Select(m =>
            {
                var h = m / 60;
                var min = m % 60;
                return $"{h:D2}:{min:D2}";
            }));
        }

        /// <summary>Compute snapped values: intervals from anchor boundaries (wrapping); each interval gets percentile of baseline in that interval.</summary>
        /// <param name="baseline">Baseline forecast per (day, slot).</param>
        /// <param name="slotMinutes">Slot duration in minutes.</param>
        /// <param name="slotsPerDay">Number of slots per day (1440 / slotMinutes).</param>
        /// <param name="anchorMinutesSorted">Sorted anchor boundaries (minutes from midnight).</param>
        /// <param name="percentile">Percentile 0-100.</param>
        /// <returns>Snapped forecast per (day, slot).</returns>
        internal static Dictionary<DayOfWeek, Dictionary<int, double>> ComputeAnchorSnap(
            Dictionary<DayOfWeek, Dictionary<int, double>> baseline,
            int slotMinutes,
            int slotsPerDay,
            List<int> anchorMinutesSorted,
            int percentile)
        {
            var days = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday };

            var intervalStarts = new List<int>();
            var intervalEnds = new List<int>();
            for (var i = 0; i < anchorMinutesSorted.Count; i++)
            {
                var start = anchorMinutesSorted[i];
                var end = anchorMinutesSorted[(i + 1) % anchorMinutesSorted.Count];
                if (start < end)
                {
                    intervalStarts.Add(start);
                    intervalEnds.Add(end);
                }
                else
                {
                    intervalStarts.Add(start);
                    intervalEnds.Add(ForecastModeHelper.MinutesPerDay);
                    intervalStarts.Add(0);
                    intervalEnds.Add(end);
                }
            }

            var valuesByInterval = new List<double>[intervalStarts.Count];
            for (var k = 0; k < intervalStarts.Count; k++)
            {
                valuesByInterval[k] = new List<double>();
            }

            foreach (var day in days)
            {
                if (!baseline.TryGetValue(day, out var bySlot))
                {
                    continue;
                }

                foreach (var kv in bySlot)
                {
                    var slotIndex = kv.Key;
                    var minuteOfDay = slotIndex * slotMinutes;
                    if (minuteOfDay >= ForecastModeHelper.MinutesPerDay)
                    {
                        continue;
                    }

                    for (var k = 0; k < intervalStarts.Count; k++)
                    {
                        var a = intervalStarts[k];
                        var b = intervalEnds[k];
                        var inInterval = a < b ? (minuteOfDay >= a && minuteOfDay < b) : (minuteOfDay >= a || minuteOfDay < b);
                        if (inInterval)
                        {
                            valuesByInterval[k].Add(kv.Value);
                            break;
                        }
                    }
                }
            }

            var percentileByInterval = new double[intervalStarts.Count];
            for (var k = 0; k < intervalStarts.Count; k++)
            {
                percentileByInterval[k] = ForecastModeHelper.PercentileValue(valuesByInterval[k], percentile);
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
                    var minuteOfDay = slotIndex * slotMinutes;
                    if (minuteOfDay >= ForecastModeHelper.MinutesPerDay)
                    {
                        continue;
                    }

                    for (var k = 0; k < intervalStarts.Count; k++)
                    {
                        var a = intervalStarts[k];
                        var b = intervalEnds[k];
                        var inInterval = a < b ? (minuteOfDay >= a && minuteOfDay < b) : (minuteOfDay >= a || minuteOfDay < b);
                        if (inInterval)
                        {
                            outBySlot[slotIndex] = percentileByInterval[k];
                            break;
                        }
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
