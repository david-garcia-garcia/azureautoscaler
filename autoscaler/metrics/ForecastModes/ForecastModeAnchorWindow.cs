using System.Globalization;
using poolautoscaler.configuration;
using poolautoscaler.metrics.Dto;

namespace poolautoscaler.metrics.ForecastModes
{
    /// <summary>AnchorWindow mode: time windows (e.g. 20:00-23:00, 03:00-07:00). Changes are only applied in configured windows; outside windows, the previous value is carried forward to avoid extra transitions.</summary>
    public sealed class ForecastModeAnchorWindow : IForecastModeStrategy
    {
        /// <inheritdoc />
        public string ModeName => "AnchorWindow";

        /// <inheritdoc />
        public bool TryApply(MetricForecastResult result, Metric metric)
        {
            var baseline = result.ValueByDayAndHour;
            if (baseline == null || !baseline.Any())
            {
                return false;
            }

            var windowRanges = ParseAnchorWindowRanges(metric.ForecastAnchorWindows);
            if (windowRanges == null || windowRanges.Count == 0)
            {
                return false;
            }

            var percentile = Math.Clamp(metric.ForecastSnapPercentile ?? 95, 0, 100);
            var slotMinutes = result.SlotMinutes;
            var slotsPerDay = (24 * 60) / slotMinutes;

            result.SnappedValueByDayAndHour = ComputeAnchorWindowSnap(baseline, slotMinutes, slotsPerDay, windowRanges, percentile);
            result.SnappedModeName = this.ModeName;
            result.SnappedPercentile = percentile;
            result.SnappedParameterSummary = FormatAnchorWindowsSummary(metric.ForecastAnchorWindows);
            return true;
        }

        /// <summary>Format anchor window strings for logging.</summary>
        /// <param name="windows">List of "HH:mm-HH:mm" window strings.</param>
        /// <returns>Comma-separated window string for logging.</returns>
        internal static string FormatAnchorWindowsSummary(List<string>? windows)
        {
            if (windows == null || windows.Count == 0)
            {
                return string.Empty;
            }

            return string.Join(", ", windows.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()));
        }

        /// <summary>Parse list of "HH:mm-HH:mm" windows to (startMin, endMin) ranges in [0, 1440). Wrapping windows are split into two ranges.</summary>
        /// <param name="windowStrings">List of "HH:mm-HH:mm" strings.</param>
        /// <returns>List of (startMin, endMin) ranges, or null if none valid.</returns>
        internal static List<(int StartMin, int EndMin)>? ParseAnchorWindowRanges(List<string>? windowStrings)
        {
            if (windowStrings == null || windowStrings.Count == 0)
            {
                return null;
            }

            var ranges = new List<(int StartMin, int EndMin)>();
            foreach (var s in windowStrings)
            {
                if (string.IsNullOrWhiteSpace(s))
                {
                    continue;
                }

                var parts = s.Trim().Split(new[] { '-' }, 2, StringSplitOptions.None);
                if (parts.Length != 2)
                {
                    continue;
                }

                if (!TimeSpan.TryParse(parts[0].Trim(), CultureInfo.InvariantCulture, out var tsStart)
                    || !TimeSpan.TryParse(parts[1].Trim(), CultureInfo.InvariantCulture, out var tsEnd))
                {
                    continue;
                }

                var startMin = (int)Math.Round(tsStart.TotalMinutes) % ForecastModeHelper.MinutesPerDay;
                if (startMin < 0)
                {
                    startMin += ForecastModeHelper.MinutesPerDay;
                }

                var endMin = (int)Math.Round(tsEnd.TotalMinutes) % ForecastModeHelper.MinutesPerDay;
                if (endMin < 0)
                {
                    endMin += ForecastModeHelper.MinutesPerDay;
                }

                if (startMin < endMin)
                {
                    ranges.Add((startMin, endMin));
                }
                else if (startMin > endMin)
                {
                    ranges.Add((startMin, ForecastModeHelper.MinutesPerDay));
                    ranges.Add((0, endMin));
                }
            }

            if (ranges.Count == 0)
            {
                return null;
            }

            return ranges;
        }

        /// <summary>Build ordered day partition: list of (startMin, endMin, isWindow).</summary>
        /// <param name="windowRanges">Window ranges in minutes from midnight.</param>
        /// <returns>Ordered segments covering the day, each marked as window or gap.</returns>
        internal static List<(int StartMin, int EndMin, bool IsWindow)> BuildDayPartition(List<(int StartMin, int EndMin)> windowRanges)
        {
            var boundaries = new SortedSet<int> { 0, ForecastModeHelper.MinutesPerDay };
            foreach (var (s, e) in windowRanges)
            {
                boundaries.Add(s);
                boundaries.Add(e);
            }

            var ordered = boundaries.ToList();
            var segments = new List<(int StartMin, int EndMin, bool IsWindow)>();
            for (var i = 0; i < ordered.Count - 1; i++)
            {
                var a = ordered[i];
                var b = ordered[i + 1];
                var isWindow = windowRanges.Any(r => a < r.EndMin && b > r.StartMin);
                segments.Add((a, b, isWindow));
            }

            return segments;
        }

        /// <summary>Compute snapped values: each window segment gets one change time T per day. From segment start to T we use the previous value; from T to segment end we use this window percentile. Non-window segments do not introduce new transitions (value is carried), except the leading 00:00 gap that keeps its own percentile as the initial day value.</summary>
        /// <param name="baseline">Baseline forecast per (day, slot).</param>
        /// <param name="slotMinutes">Slot duration in minutes.</param>
        /// <param name="slotsPerDay">Number of slots per day.</param>
        /// <param name="windowRanges">Window ranges (startMin, endMin).</param>
        /// <param name="percentile">Percentile 0-100.</param>
        /// <returns>Snapped forecast per (day, slot).</returns>
        internal static Dictionary<DayOfWeek, Dictionary<int, double>> ComputeAnchorWindowSnap(
            Dictionary<DayOfWeek, Dictionary<int, double>> baseline,
            int slotMinutes,
            int slotsPerDay,
            List<(int StartMin, int EndMin)> windowRanges,
            int percentile)
        {
            var days = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday };
            var segments = BuildDayPartition(windowRanges);

            // Per-day segment values and percentiles (so T can differ by day).
            var segmentValuesByDay = new Dictionary<DayOfWeek, List<double>[]>();
            foreach (var day in days)
            {
                if (!baseline.TryGetValue(day, out var bySlot))
                {
                    continue;
                }

                var segVals = new List<double>[segments.Count];
                for (var k = 0; k < segments.Count; k++)
                {
                    segVals[k] = new List<double>();
                }

                foreach (var kv in bySlot)
                {
                    var slotIndex = kv.Key;
                    var minuteOfDay = slotIndex * slotMinutes;
                    if (minuteOfDay >= ForecastModeHelper.MinutesPerDay)
                    {
                        continue;
                    }

                    for (var k = 0; k < segments.Count; k++)
                    {
                        var (a, b, _) = segments[k];
                        if (minuteOfDay >= a && minuteOfDay < b)
                        {
                            segVals[k].Add(kv.Value);
                            break;
                        }
                    }
                }

                segmentValuesByDay[day] = segVals;
            }

            // Per-day segment percentiles.
            var segmentPercentileByDay = new Dictionary<DayOfWeek, double[]>();
            foreach (var (day, segVals) in segmentValuesByDay)
            {
                var segP = new double[segments.Count];
                for (var k = 0; k < segments.Count; k++)
                {
                    segP[k] = ForecastModeHelper.PercentileValue(segVals[k], percentile);
                }

                // Merge the wrapping gap for this day: 23:00-24:00 and 00:00-03:00 use one percentile.
                if (segments.Count >= 2)
                {
                    var first = segments[0];
                    var last = segments[segments.Count - 1];
                    if (!first.IsWindow && !last.IsWindow && first.StartMin == 0 && last.EndMin == ForecastModeHelper.MinutesPerDay)
                    {
                        var combined = new List<double>(segVals[0]);
                        combined.AddRange(segVals[segments.Count - 1]);
                        var mergedP = ForecastModeHelper.PercentileValue(combined, percentile);
                        segP[0] = mergedP;
                        segP[segments.Count - 1] = mergedP;
                    }
                }

                segmentPercentileByDay[day] = segP;
            }

            // Index of last window segment (for wrapping gap: 00:00-03:00 should step down from evening window value, not from 23:00-24:00 gap).
            var lastWindowSegmentIndex = -1;
            if (segments.Count >= 2)
            {
                var first = segments[0];
                var last = segments[segments.Count - 1];
                if (!first.IsWindow && !last.IsWindow && first.StartMin == 0 && last.EndMin == ForecastModeHelper.MinutesPerDay)
                {
                    for (var i = segments.Count - 1; i >= 0; i--)
                    {
                        if (segments[i].IsWindow)
                        {
                            lastWindowSegmentIndex = i;
                            break;
                        }
                    }
                }
            }

            // Per-day, per-segment change time T. Windows: one value per window, starting at segment boundary.
            // Gaps do not define a change time; they carry the previous value (except the leading 00:00 gap).
            var changeTimeMinByDay = new Dictionary<DayOfWeek, int?[]>();
            foreach (var (day, segP) in segmentPercentileByDay)
            {
                if (!baseline.TryGetValue(day, out var bySlot))
                {
                    continue;
                }

                var changeTimeMin = new int?[segments.Count];
                for (var k = 0; k < segments.Count; k++)
                {
                    var (a, b, isWindow) = segments[k];
                    if (isWindow)
                    {
                        // One value per window: use window percentile from segment start (no internal change).
                        changeTimeMin[k] = a;
                        continue;
                    }

                    changeTimeMin[k] = null;
                }

                changeTimeMinByDay[day] = changeTimeMin;
            }

            var snapped = new Dictionary<DayOfWeek, Dictionary<int, double>>();
            double? previousDayLastValue = null;
            foreach (var day in days)
            {
                if (!baseline.TryGetValue(day, out var bySlot) || !segmentPercentileByDay.TryGetValue(day, out var segP) || !changeTimeMinByDay.TryGetValue(day, out var changeTimeMin))
                {
                    continue;
                }

                var outBySlot = new Dictionary<int, double>();
                foreach (var slotIndex in bySlot.Keys.OrderBy(x => x))
                {
                    var minuteOfDay = slotIndex * slotMinutes;
                    if (minuteOfDay >= ForecastModeHelper.MinutesPerDay)
                    {
                        continue;
                    }

                    for (var k = 0; k < segments.Count; k++)
                    {
                        var (a, b, isWindow) = segments[k];
                        if (minuteOfDay < a || minuteOfDay >= b)
                        {
                            continue;
                        }

                        if (!isWindow)
                        {
                            // Keep non-window segments flat by carrying previous value.
                            // For day-start gap (00:00..), carry value from previous day to avoid a midnight jump.
                            if (k == 0 && a == 0)
                            {
                                outBySlot[slotIndex] = previousDayLastValue ?? segP[k];
                            }
                            else
                            {
                                var prevK = (k == 0 && lastWindowSegmentIndex >= 0) ? lastWindowSegmentIndex : (k - 1 + segments.Count) % segments.Count;
                                outBySlot[slotIndex] = segP[prevK];
                            }
                        }
                        else
                        {
                            var tOpt = changeTimeMin[k];
                            if (tOpt.HasValue)
                            {
                                var t = tOpt.GetValueOrDefault();
                                var prevK = (k == 0 && lastWindowSegmentIndex >= 0) ? lastWindowSegmentIndex : (k - 1 + segments.Count) % segments.Count;
                                outBySlot[slotIndex] = minuteOfDay < t ? segP[prevK] : segP[k];
                            }
                            else
                            {
                                outBySlot[slotIndex] = segP[k];
                            }
                        }

                        break;
                    }
                }

                if (outBySlot.Any())
                {
                    snapped[day] = outBySlot;
                    previousDayLastValue = outBySlot.OrderBy(kv => kv.Key).Last().Value;
                }
            }

            // Ensure week continuity: Sunday -> Monday should not jump at midnight when 00:00 belongs to a non-window gap.
            if (snapped.TryGetValue(DayOfWeek.Sunday, out var sundayOut)
                && sundayOut.Any()
                && snapped.TryGetValue(DayOfWeek.Monday, out var mondayOut)
                && mondayOut.Any()
                && segments.Any())
            {
                var firstSegment = segments[0];
                if (!firstSegment.IsWindow && firstSegment.StartMin == 0)
                {
                    var sundayLastValue = sundayOut.OrderBy(kv => kv.Key).Last().Value;
                    foreach (var slotIndex in mondayOut.Keys.OrderBy(x => x).ToList())
                    {
                        var minuteOfDay = slotIndex * slotMinutes;
                        if (minuteOfDay >= firstSegment.StartMin && minuteOfDay < firstSegment.EndMin)
                        {
                            mondayOut[slotIndex] = sundayLastValue;
                        }
                    }
                }
            }

            return snapped;
        }
    }
}
