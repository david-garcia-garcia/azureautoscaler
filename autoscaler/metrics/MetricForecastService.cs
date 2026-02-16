using Azure.Monitor.Query;
using Azure.Monitor.Query.Models;
using Microsoft.Extensions.Logging;
using poolautoscaler.configuration;
using poolautoscaler.metrics.Dto;
using poolautoscaler.metrics.ForecastModes;
using poolautoscaler.resourcemanagement;

namespace poolautoscaler.metrics
{
    /// <summary>
    /// Computes a simple forecast for any metric from history.
    /// When the observed value is constrained by a max-available metric (capped), the forecast is flagged as potentially underestimated.
    /// </summary>
    internal sealed class MetricForecastService
    {
        private const double CapThreshold = 0.95; // Consider capped when value >= 95% of max at that time
        private const double MarginMinutes = 15; // Match points within 15 minutes when aligning series
        private const int MinSlotMinutes = 15;
        private const int MaxSlotMinutes = 60;
        private const int MaxColumnsPerTable = 20;

        private readonly ILogger logger;
        private readonly IReadOnlyList<IForecastModeStrategy> forecastModeStrategies;

        /// <summary>Initializes a new instance of the <see cref="MetricForecastService"/> class.</summary>
        /// <param name="logger">The logger instance.</param>
        /// <param name="forecastModeStrategies">Optional forecast mode strategies (Anchors, AnchorWindow, Snap, Raw). If null, defaults to built-in strategies.</param>
        public MetricForecastService(ILogger logger, IReadOnlyList<IForecastModeStrategy>? forecastModeStrategies = null)
        {
            this.logger = logger;
            this.forecastModeStrategies = forecastModeStrategies ?? new IForecastModeStrategy[]
            {
                new ForecastModeAnchor(),
                new ForecastModeAnchorWindow(),
                new ForecastModeSnap(),
                new ForecastModeRaw()
            };
        }

        /// <summary>
        /// Fetches history and computes the full forecast (all days of week). Expensive; call infrequently and cache the result. Use <see cref="GetCurrentForecastValue"/> with the cached result to get the value to apply for the current time.
        /// </summary>
        /// <param name="state">The resource state.</param>
        /// <param name="metric">The metric configuration with ForecastEnable and forecast parameters.</param>
        /// <param name="setting">Scaling configuration.</param>
        /// <param name="client">Metrics query client.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The cacheable full forecast, or null if disabled / no data / error.</returns>
        public async Task<MetricForecastResult?> GetFullForecastAsync(
            ResourceState state,
            Metric metric,
            ScalingConfiguration setting,
            MetricsQueryClient client,
            CancellationToken cancellationToken)
        {
            if (!metric.ForecastEnable || !metric.ForecastTimeRangeParsed.HasValue)
            {
                return null;
            }

            if (string.IsNullOrEmpty(metric.ForecastMetricMax))
            {
                this.logger.LogWarning("Forecast enabled for metric {MetricId} but ForecastMetricMax is not set.", metric.Id);
                return null;
            }

            var timeRange = metric.ForecastTimeRangeParsed.Value;
            var slotMinutes = ResolveSlotMinutes(metric.ForecastSlotMinutes);
            var granularityMinutes = Math.Min(metric.ForecastMetricsGranularityMinutes ?? 60, slotMinutes);
            var granularity = TimeSpan.FromMinutes(granularityMinutes);
            var resourceId = state.ReplaceResourceParts(metric.ResourceId ?? state.Resource.Id);
            var splitName = state.ReplaceResourceParts(metric.SplitName);
            var splitValue = state.ReplaceResourceParts(metric.SplitValue);

            var aggregations = metric.ParsedAggregations ?? new List<MetricAggregationType> { MetricAggregationType.Average, MetricAggregationType.Maximum };
            if (!aggregations.Contains(MetricAggregationType.Maximum))
            {
                aggregations = new List<MetricAggregationType>(aggregations) { MetricAggregationType.Maximum };
            }

            var eval = new MetricEvaluation(this.logger);
            var now = DateTime.UtcNow;
            var alignToHour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc);

            var mainResult = await eval.RetrieveHistoryRaw(
                client,
                resourceId,
                metric.Name,
                timeRange,
                granularity,
                cancellationToken,
                splitName,
                splitValue,
                aggregations,
                alignToHour);

            if (mainResult?.Values == null || !mainResult.Values.Any())
            {
                this.logger.LogDebug("No history for metric {MetricId} ({Name}); cannot produce forecast.", metric.Id, metric.Name);
                return null;
            }

            var maxMetricName = ResolveMaxMetricName(metric, setting);
            if (string.IsNullOrEmpty(maxMetricName))
            {
                return null;
            }

            List<MetricEvalDtoResultValue> maxSeries;
            try
            {
                var maxResult = await eval.RetrieveHistoryRaw(
                    client,
                    resourceId,
                    maxMetricName,
                    timeRange,
                    granularity,
                    cancellationToken,
                    splitName,
                    splitValue,
                    new List<MetricAggregationType> { MetricAggregationType.Average, MetricAggregationType.Maximum },
                    alignToHour);
                if (maxResult?.Values == null || !maxResult.Values.Any())
                {
                    return null;
                }

                maxSeries = maxResult.Values;
                if (setting?.Metrics != null && setting.Metrics.TryGetValue(metric.ForecastMetricMax, out var maxMetricConfig) && maxMetricConfig.TransformExpression != null)
                {
                    maxSeries = maxSeries.Select(maxMetricConfig.TransformExpression).ToList();
                }
            }
            catch (Exception ex)
            {
                this.logger.LogWarning(ex, "Could not load max metric {MaxMetric} for forecast.", metric.ForecastMetricMax);
                return null;
            }

            var startDate = now.Add(-timeRange);
            var sameDay = metric.ForecastAffinitySameDayFactor ?? 1.0;
            var weekday = metric.ForecastAffinityWeekdayFactor ?? 0.3;
            var weekend = metric.ForecastAffinityWeekendFactor ?? 0.3;
            var fullForecast = this.ComputeFullForecastFromHistory(
                mainResult.Values,
                maxSeries,
                startDate,
                now,
                aggregations,
                metric.Id,
                sameDay,
                weekday,
                weekend,
                slotMinutes);
            if (fullForecast != null)
            {
                this.ApplySnap(fullForecast, metric);
                if (fullForecast.SnappedValueByDayAndHour != null)
                {
                    this.LogSnappedForecastTable(fullForecast);
                }
            }

            return fullForecast;
        }

        /// <summary>
        /// Computes a forecast for the given metric (fetches history, then returns current value). Prefer caching: use <see cref="GetFullForecastAsync"/> and <see cref="GetCurrentForecastValue"/> so the expensive step is done once.
        /// </summary>
        /// <param name="state">The resource state (used for resource id and ReplaceResourceParts).</param>
        /// <param name="metric">The metric configuration with ForecastEnable and forecast parameters.</param>
        /// <param name="setting">Scaling configuration (used to resolve ForecastMetricMax by id).</param>
        /// <param name="client">Metrics query client.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Forecast result with one value for "now". Null if forecast not enabled or parsing missing.</returns>
        public async Task<MetricEvalDtoResult?> ComputeForecastAsync(
            ResourceState state,
            Metric metric,
            ScalingConfiguration setting,
            MetricsQueryClient client,
            CancellationToken cancellationToken)
        {
            if (!metric.ForecastEnable || !metric.ForecastTimeRangeParsed.HasValue)
            {
                return null;
            }

            if (string.IsNullOrEmpty(metric.ForecastMetricMax))
            {
                this.logger.LogWarning("Forecast enabled for metric {MetricId} but ForecastMetricMax is not set; forecast requires a max metric to scale against.", metric.Id);
                return new MetricEvalDtoResult
                {
                    Values = new List<MetricEvalDtoResultValue>(),
                    Valid = false,
                    InvalidReason = "ForecastMetricMax is required when ForecastEnable is true (the max metric we scale against must be known).",
                    ExecutedAggregations = metric.ParsedAggregations ?? new List<MetricAggregationType> { MetricAggregationType.Average, MetricAggregationType.Maximum }
                };
            }

            var timeRange = metric.ForecastTimeRangeParsed.Value;
            var slotMinutes = ResolveSlotMinutes(metric.ForecastSlotMinutes);
            var granularityMinutes = Math.Min(metric.ForecastMetricsGranularityMinutes ?? 60, slotMinutes);
            var granularity = TimeSpan.FromMinutes(granularityMinutes);
            var resourceId = state.ReplaceResourceParts(metric.ResourceId ?? state.Resource.Id);
            var splitName = state.ReplaceResourceParts(metric.SplitName);
            var splitValue = state.ReplaceResourceParts(metric.SplitValue);

            var aggregations = metric.ParsedAggregations ?? new List<MetricAggregationType> { MetricAggregationType.Average, MetricAggregationType.Maximum };
            if (!aggregations.Contains(MetricAggregationType.Maximum))
            {
                aggregations = new List<MetricAggregationType>(aggregations) { MetricAggregationType.Maximum };
            }

            var eval = new MetricEvaluation(this.logger);
            var now = DateTime.UtcNow;
            var alignToHour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc);

            var mainResult = await eval.RetrieveHistoryRaw(
                client,
                resourceId,
                metric.Name,
                timeRange,
                granularity,
                cancellationToken,
                splitName,
                splitValue,
                aggregations,
                alignToHour);

            if (mainResult?.Values == null || !mainResult.Values.Any())
            {
                this.logger.LogDebug("No history for metric {MetricId} ({Name}); cannot produce forecast.", metric.Id, metric.Name);
                return new MetricEvalDtoResult
                {
                    Values = new List<MetricEvalDtoResultValue>(),
                    Valid = false,
                    InvalidReason = "No metric history available for forecast.",
                    ExecutedAggregations = aggregations
                };
            }

            var maxMetricName = ResolveMaxMetricName(metric, setting);
            if (string.IsNullOrEmpty(maxMetricName))
            {
                this.logger.LogWarning("ForecastMetricMax '{MaxMetric}' could not be resolved to a metric name for {MetricId}.", metric.ForecastMetricMax, metric.Id);
                return new MetricEvalDtoResult
                {
                    Values = new List<MetricEvalDtoResultValue>(),
                    Valid = false,
                    InvalidReason = "ForecastMetricMax could not be resolved to a metric name.",
                    ExecutedAggregations = aggregations
                };
            }

            List<MetricEvalDtoResultValue> maxSeries;
            try
            {
                var maxResult = await eval.RetrieveHistoryRaw(
                    client,
                    resourceId,
                    maxMetricName,
                    timeRange,
                    granularity,
                    cancellationToken,
                    splitName,
                    splitValue,
                    new List<MetricAggregationType> { MetricAggregationType.Average, MetricAggregationType.Maximum },
                    alignToHour);
                if (maxResult?.Values == null || !maxResult.Values.Any())
                {
                    this.logger.LogWarning("No history for max metric {MaxMetric} ({ResolvedName}); forecast requires max series.", metric.ForecastMetricMax, maxMetricName);
                    return new MetricEvalDtoResult
                    {
                        Values = new List<MetricEvalDtoResultValue>(),
                        Valid = false,
                        InvalidReason = "No history available for ForecastMetricMax; the max we scale against must be known.",
                        ExecutedAggregations = aggregations
                    };
                }

                maxSeries = maxResult.Values;
                if (setting?.Metrics != null && setting.Metrics.TryGetValue(metric.ForecastMetricMax, out var maxMetricConfig) && maxMetricConfig.TransformExpression != null)
                {
                    maxSeries = maxSeries.Select(maxMetricConfig.TransformExpression).ToList();
                }
            }
            catch (Exception ex)
            {
                this.logger.LogWarning(ex, "Could not load max metric {MaxMetric} for forecast: {Message}", metric.ForecastMetricMax, ex.Message);
                return new MetricEvalDtoResult
                {
                    Values = new List<MetricEvalDtoResultValue>(),
                    Valid = false,
                    InvalidReason = $"Could not load ForecastMetricMax '{metric.ForecastMetricMax}': {ex.Message}",
                    ExecutedAggregations = aggregations
                };
            }

            // Expensive: compute full forecast (all days of week); then cheap: select value for "now".
            var startDate = now.Add(-timeRange);
            var sameDay = metric.ForecastAffinitySameDayFactor ?? 1.0;
            var weekday = metric.ForecastAffinityWeekdayFactor ?? 0.3;
            var weekend = metric.ForecastAffinityWeekendFactor ?? 0.3;
            var fullForecast = this.ComputeFullForecastFromHistory(
                mainResult.Values,
                maxSeries,
                startDate,
                now,
                aggregations,
                metric.Id,
                sameDay,
                weekday,
                weekend,
                slotMinutes);
            if (fullForecast == null)
            {
                return null;
            }

            this.ApplySnap(fullForecast, metric);
            return this.GetCurrentForecastValue(fullForecast, now);
        }

        /// <summary>
        /// Returns the forecast value to apply at a given time from a pre-computed full forecast (cheap; use for caching).
        /// </summary>
        /// <param name="fullForecast">The cacheable full forecast from <see cref="ComputeFullForecastFromHistory"/> or <see cref="GetFullForecastAsync"/>.</param>
        /// <param name="referenceTimeUtc">The time for which to get the forecast (typically DateTime.UtcNow).</param>
        /// <returns>Single-value metric result for the given time, or invalid result if no forecast for that day of week.</returns>
        internal MetricEvalDtoResult GetCurrentForecastValue(MetricForecastResult fullForecast, DateTimeOffset referenceTimeUtc)
        {
            var dayOfWeek = referenceTimeUtc.DayOfWeek;
            var slotMinutes = fullForecast.SlotMinutes;
            var dayStart = new DateTimeOffset(referenceTimeUtc.UtcDateTime.Date, TimeSpan.Zero);
            var minutesFromMidnight = (referenceTimeUtc - dayStart).TotalMinutes;
            var slotsPerDay = (24 * 60) / slotMinutes;
            var slotIndex = (int)(minutesFromMidnight / slotMinutes);
            if (slotIndex < 0)
            {
                slotIndex = 0;
            }
            else if (slotIndex >= slotsPerDay)
            {
                slotIndex = slotsPerDay - 1;
            }

            var sourceBySlot = fullForecast.SnappedValueByDayAndHour != null && fullForecast.SnappedValueByDayAndHour.TryGetValue(dayOfWeek, out var snapped) && snapped.Any()
                ? snapped
                : fullForecast.ValueByDayAndHour.TryGetValue(dayOfWeek, out var baseline) ? baseline : null;

            if (sourceBySlot == null || !sourceBySlot.Any())
            {
                this.logger.LogDebug("No forecast for day of week {DayOfWeek} for metric {MetricId}.", dayOfWeek, fullForecast.MetricId);
                return new MetricEvalDtoResult
                {
                    Values = new List<MetricEvalDtoResultValue>(),
                    Valid = false,
                    InvalidReason = "No compatible historical windows for this day of week.",
                    ExecutedAggregations = fullForecast.ExecutedAggregations
                };
            }

            // Use value for (day, slot) if present; otherwise fall back to max over that day
            var value = sourceBySlot.TryGetValue(slotIndex, out var atSlot) ? atSlot : sourceBySlot.Values.Max();
            var anyCapped = fullForecast.CappedByDayAndHour.TryGetValue(dayOfWeek, out var cappedBySlot) && cappedBySlot.TryGetValue(slotIndex, out var capped) && capped;
            if (!anyCapped && fullForecast.CappedByDayAndHour.TryGetValue(dayOfWeek, out var dayCapped) && dayCapped.Values.Any(c => c))
            {
                anyCapped = true; // fallback to day max: still mark capped if any slot that day was capped
            }

            return new MetricEvalDtoResult
            {
                ExecutedAggregations = fullForecast.ExecutedAggregations,
                Values = new List<MetricEvalDtoResultValue>
                {
                    new MetricEvalDtoResultValue
                    {
                        TimeStamp = DateTimeOffset.UtcNow,
                        Average = value,
                        Maximum = value,
                        Default = value,
                        Valid = !anyCapped,
                        InvalidReason = anyCapped
                            ? "Forecast may be underestimated: metric was constrained by max_available_metric at some points."
                            : null
                    }
                },
                Valid = !anyCapped,
                InvalidReason = anyCapped
                    ? "Forecast may be underestimated: metric was constrained by max at some points."
                    : null
            };
        }

        /// <summary>
        /// Computes the full forecast (all days of week) from pre-fetched history. Expensive; result is cacheable. Use <see cref="GetCurrentForecastValue"/> to get the value to apply for a given time.
        /// </summary>
        /// <param name="mainValues">Main metric time series.</param>
        /// <param name="maxSeries">Max/cap metric time series.</param>
        /// <param name="startDate">Start of analysis range (UTC).</param>
        /// <param name="endDate">End of analysis range (UTC).</param>
        /// <param name="aggregations">Aggregations list for the result.</param>
        /// <param name="metricId">Metric id for logging.</param>
        /// <param name="sameDayFactor">Affinity weight for same day of week. Default 1.0.</param>
        /// <param name="weekdayFactor">Affinity weight for weekday-to-weekday. Default 0.3.</param>
        /// <param name="weekendFactor">Affinity weight for weekend-to-weekend. Default 0.3.</param>
        /// <param name="slotMinutes">Slot interval in minutes (15, 30, or 60). Default 60.</param>
        /// <returns>Cacheable full forecast, or null if no daily windows.</returns>
        internal MetricForecastResult? ComputeFullForecastFromHistory(
            List<MetricEvalDtoResultValue> mainValues,
            List<MetricEvalDtoResultValue> maxSeries,
            DateTimeOffset startDate,
            DateTimeOffset endDate,
            IList<MetricAggregationType> aggregations,
            string metricId,
            double sameDayFactor = 1.0,
            double weekdayFactor = 0.3,
            double weekendFactor = 0.3,
            int slotMinutes = 60)
        {
            slotMinutes = ResolveSlotMinutes(slotMinutes);
            var slotsPerDay = (24 * 60) / slotMinutes;
            var dailyWindows = this.BuildDailyWindows(mainValues, maxSeries, startDate, endDate, slotMinutes);
            if (!dailyWindows.Any())
            {
                this.logger.LogDebug("No daily windows with data for metric {MetricId}; cannot produce forecast.", metricId);
                return null;
            }

            var valueByDayAndHour = new Dictionary<DayOfWeek, Dictionary<int, double>>();
            var cappedByDayAndHour = new Dictionary<DayOfWeek, Dictionary<int, bool>>();
            foreach (DayOfWeek dayOfWeek in Enum.GetValues(typeof(DayOfWeek)))
            {
                var compatible = dailyWindows.Where(w => CalculateAffinity(dayOfWeek, w.DayOfWeek, sameDayFactor, weekdayFactor, weekendFactor) > 0.5).ToList();
                if (compatible.Any())
                {
                    var bySlot = new Dictionary<int, double>();
                    var cappedBySlot = new Dictionary<int, bool>();
                    for (int s = 0; s < slotsPerDay; s++)
                    {
                        var valuesAtSlot = compatible
                            .Where(w => w.ValueByHour != null && w.ValueByHour.ContainsKey(s))
                            .Select(w => w.ValueByHour[s])
                            .ToList();
                        if (valuesAtSlot.Any())
                        {
                            bySlot[s] = valuesAtSlot.Max();
                            var anyCappedAtSlot = compatible
                                .Where(w => w.CappedByHour != null && w.CappedByHour.ContainsKey(s) && w.CappedByHour[s])
                                .Any();
                            cappedBySlot[s] = anyCappedAtSlot;
                        }
                    }

                    if (bySlot.Any())
                    {
                        valueByDayAndHour[dayOfWeek] = bySlot;
                        cappedByDayAndHour[dayOfWeek] = cappedBySlot;
                    }
                }
            }

            var fullForecast = new MetricForecastResult
            {
                SlotMinutes = slotMinutes,
                ValueByDayAndHour = valueByDayAndHour,
                CappedByDayAndHour = cappedByDayAndHour,
                ExpiresAtUtc = DateTime.UtcNow.AddHours(1),
                MetricId = metricId,
                ExecutedAggregations = aggregations
            };

            var buildInfo = new ForecastBuildInfo(
                startDate,
                endDate,
                dailyWindows.Count,
                dailyWindows.GroupBy(w => w.DayOfWeek).ToDictionary(g => g.Key, g => g.Count()));
            this.LogFullWeeklyForecast(fullForecast, buildInfo);
            return fullForecast;
        }

        /// <summary>
        /// Computes forecast from pre-fetched history (full forecast + current value). Used by unit tests. For production caching, use <see cref="ComputeFullForecastFromHistory"/> then <see cref="GetCurrentForecastValue"/>.
        /// </summary>
        /// <param name="mainValues">Main metric time series.</param>
        /// <param name="maxSeries">Max/cap metric time series.</param>
        /// <param name="startDate">Start of analysis range (UTC).</param>
        /// <param name="endDate">End of analysis range (UTC).</param>
        /// <param name="referenceTimeUtc">Reference time for "today's" day of week.</param>
        /// <param name="aggregations">Aggregations list for the result.</param>
        /// <param name="metricId">Metric id for logging.</param>
        /// <param name="sameDayFactor">Affinity for same day (used when no Metric config, e.g. tests). Default 1.0.</param>
        /// <param name="weekdayFactor">Affinity for weekday-to-weekday. Default 0.3.</param>
        /// <param name="weekendFactor">Affinity for weekend-to-weekend. Default 0.3.</param>
        /// <returns>Single-value forecast result for the reference time, or invalid result if no data.</returns>
        internal MetricEvalDtoResult? ComputeForecastFromHistory(
            List<MetricEvalDtoResultValue> mainValues,
            List<MetricEvalDtoResultValue> maxSeries,
            DateTimeOffset startDate,
            DateTimeOffset endDate,
            DateTimeOffset referenceTimeUtc,
            IList<MetricAggregationType> aggregations,
            string metricId,
            double sameDayFactor = 1.0,
            double weekdayFactor = 0.3,
            double weekendFactor = 0.3)
        {
            var fullForecast = this.ComputeFullForecastFromHistory(mainValues, maxSeries, startDate, endDate, aggregations, metricId, sameDayFactor, weekdayFactor, weekendFactor);
            if (fullForecast == null)
            {
                return new MetricEvalDtoResult
                {
                    Values = new List<MetricEvalDtoResultValue>(),
                    Valid = false,
                    InvalidReason = "No data in history for forecast.",
                    ExecutedAggregations = aggregations
                };
            }

            return this.GetCurrentForecastValue(fullForecast, referenceTimeUtc);
        }

        /// <summary>Logs the full weekly forecast (every day of week, every slot) at Debug level, plus reliability summary. Tables are split into at most <see cref="MaxColumnsPerTable"/> columns each.</summary>
        private void LogFullWeeklyForecast(MetricForecastResult forecast, ForecastBuildInfo? buildInfo = null)
        {
            if (!this.LogFullWeeklyForecastEnabled())
            {
                return;
            }

            const int DayColumnWidth = 4;
            const int CellWidth = 7;
            var days = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday };
            var slotMinutes = forecast.SlotMinutes;
            var slotsPerDay = (24 * 60) / slotMinutes;
            this.logger.LogDebug(
                "---------- Full weekly forecast (projected value per day and slot UTC, {SlotMin}min slots): {MetricId} ----------",
                slotMinutes,
                forecast.MetricId);

            if (buildInfo != null)
            {
                var weeksAnalyzed = buildInfo.TotalWeeksAnalyzed;
                this.logger.LogDebug(
                    "Reliability: Analyzed {Weeks:F1} weeks ({Start:yyyy-MM-dd} to {End:yyyy-MM-dd} UTC). {DaysWithData} days had data.",
                    weeksAnalyzed,
                    buildInfo.StartDateUtc,
                    buildInfo.EndDateUtc,
                    buildInfo.DaysWithDataCount);
                var samplesByDay = string.Join(", ", days.Select(d => buildInfo.SamplesPerDayOfWeek.TryGetValue(d, out var n) ? $"{d}: {n}" : $"{d}: 0"));
                this.logger.LogDebug("Samples per day of week: {SamplesByDay}.", samplesByDay);
                var slotsWithData = forecast.ValueByDayAndHour.Values.Select(d => d.Count).ToList();
                if (slotsWithData.Any())
                {
                    this.logger.LogDebug(
                        "Forecast coverage: each day has between {MinSlots} and {MaxSlots} slots with data (of {SlotsPerDay}).",
                        slotsWithData.Min(),
                        slotsWithData.Max(),
                        slotsPerDay);
                }
            }

            for (var colStart = 0; colStart < slotsPerDay; colStart += MaxColumnsPerTable)
            {
                var colCount = Math.Min(MaxColumnsPerTable, slotsPerDay - colStart);
                var header = "Day".PadRight(DayColumnWidth) + string.Join(
                    string.Empty,
                    Enumerable.Range(0, colCount).Select(i => FormatSlotLabel(colStart + i, slotMinutes).PadLeft(CellWidth)));
                this.logger.LogDebug(header);
                foreach (var day in days)
                {
                    var bySlot = forecast.ValueByDayAndHour.TryGetValue(day, out var d) ? d : null;
                    var row = FormatDayOfWeekShort(day).PadRight(DayColumnWidth);
                    for (var i = 0; i < colCount; i++)
                    {
                        var slot = colStart + i;
                        var val = (bySlot != null && bySlot.TryGetValue(slot, out var v)) ? v.ToString("F0") : "-";
                        row += val.PadLeft(CellWidth);
                    }

                    this.logger.LogDebug(row);
                }

                if (colStart + colCount < slotsPerDay)
                {
                    this.logger.LogDebug(string.Empty);
                }
            }

            this.logger.LogDebug("---------- End full weekly forecast ----------");
        }

        /// <summary>Logs the snapped forecast table (same format as baseline) right after the regular forecast. Only called when SnappedValueByDayAndHour is set.</summary>
        private void LogSnappedForecastTable(MetricForecastResult forecast)
        {
            if (forecast.SnappedValueByDayAndHour == null || !this.LogFullWeeklyForecastEnabled())
            {
                return;
            }

            const int DayColumnWidth = 4;
            const int CellWidth = 7;
            var days = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday };
            var slotMinutes = forecast.SlotMinutes;
            var slotsPerDay = (24 * 60) / slotMinutes;
            var modeName = string.IsNullOrEmpty(forecast.SnappedModeName) ? "Forecast" : forecast.SnappedModeName;
            var percentileStr = forecast.SnappedPercentile.HasValue ? $"percentile {forecast.SnappedPercentile.Value}" : null;
            var paramsStr = string.IsNullOrEmpty(forecast.SnappedParameterSummary) ? null : forecast.SnappedParameterSummary;
            var details = new List<string> { modeName };
            if (!string.IsNullOrEmpty(percentileStr))
            {
                details.Add(percentileStr);
            }

            if (!string.IsNullOrEmpty(paramsStr))
            {
                details.Add(paramsStr);
            }

            details.Add($"{slotMinutes}min slots");
            var detailsLine = string.Join(", ", details);
            this.logger.LogDebug(
                "---------- Snapped forecast ({Details}): {MetricId} ----------",
                detailsLine,
                forecast.MetricId);

            for (var colStart = 0; colStart < slotsPerDay; colStart += MaxColumnsPerTable)
            {
                var colCount = Math.Min(MaxColumnsPerTable, slotsPerDay - colStart);
                var header = "Day".PadRight(DayColumnWidth) + string.Join(
                    string.Empty,
                    Enumerable.Range(0, colCount).Select(i => FormatSlotLabel(colStart + i, slotMinutes).PadLeft(CellWidth)));
                this.logger.LogDebug(header);
                foreach (var day in days)
                {
                    var bySlot = forecast.SnappedValueByDayAndHour.TryGetValue(day, out var d) ? d : null;
                    var row = FormatDayOfWeekShort(day).PadRight(DayColumnWidth);
                    for (var i = 0; i < colCount; i++)
                    {
                        var slot = colStart + i;
                        var val = (bySlot != null && bySlot.TryGetValue(slot, out var v)) ? v.ToString("F0") : "-";
                        row += val.PadLeft(CellWidth);
                    }

                    this.logger.LogDebug(row);
                }

                if (colStart + colCount < slotsPerDay)
                {
                    this.logger.LogDebug(string.Empty);
                }
            }

            this.logger.LogDebug("---------- End snapped forecast ----------");
        }

        /// <summary>Fills SnappedValueByDayAndHour from baseline (ValueByDayAndHour) using metric's snap config. Does not modify baseline.</summary>
        private void ApplySnap(MetricForecastResult result, Metric metric)
        {
            var mode = (metric.ForecastMode ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(mode) || string.Equals(mode, "Raw", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var baseline = result.ValueByDayAndHour;
            if (baseline == null || !baseline.Any())
            {
                return;
            }

            var strategy = this.forecastModeStrategies.FirstOrDefault(s => string.Equals(s.ModeName, mode, StringComparison.OrdinalIgnoreCase));
            if (strategy == null)
            {
                this.logger.LogDebug("ForecastMode={Mode} not recognized; use Raw, Anchors, AnchorWindow, or Snap.", mode);
                return;
            }

            if (!strategy.TryApply(result, metric))
            {
                this.logger.LogDebug("ForecastMode={Mode} but config invalid or missing; skipping snap.", mode);
            }
        }

        private static string FormatDayOfWeekShort(DayOfWeek day)
        {
            return day switch
            {
                DayOfWeek.Monday => "Mon",
                DayOfWeek.Tuesday => "Tue",
                DayOfWeek.Wednesday => "Wed",
                DayOfWeek.Thursday => "Thu",
                DayOfWeek.Friday => "Fri",
                DayOfWeek.Saturday => "Sat",
                DayOfWeek.Sunday => "Sun",
                _ => day.ToString()[..3]
            };
        }

        private static string FormatSlotLabel(int slotIndex, int slotMinutes)
        {
            var totalMinutes = slotIndex * slotMinutes;
            var h = totalMinutes / 60;
            var m = totalMinutes % 60;
            return slotMinutes < 60 ? $"{h:D2}:{m:D2}" : h.ToString("D2", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>Override in tests to enable full weekly forecast debug logging. Production uses LogDebug so it depends on log level.</summary>
        private bool LogFullWeeklyForecastEnabled()
        {
            return true;
        }

        /// <summary>Builds daily windows: for each day in range, uses all points in that calendar day (full 24h) and computes per-slot max and capped flag.</summary>
        private List<DailyWindowResult> BuildDailyWindows(
            List<MetricEvalDtoResultValue> mainValues,
            List<MetricEvalDtoResultValue> maxSeries,
            DateTimeOffset startDate,
            DateTimeOffset endDate,
            int slotMinutes)
        {
            var slotsPerDay = (24 * 60) / slotMinutes;
            var results = new List<DailyWindowResult>();
            for (var date = new DateTimeOffset(startDate.Date, TimeSpan.Zero); date <= endDate; date = date.AddDays(1))
            {
                var windowStart = date;
                var windowEnd = date.AddDays(1);

                var pointsInWindow = mainValues
                    .Where(v => v.TimeStamp >= windowStart && v.TimeStamp < windowEnd)
                    .ToList();
                if (!pointsInWindow.Any())
                {
                    continue;
                }

                double? windowMax = null;
                var windowCapped = false;
                var valueBySlot = new Dictionary<int, double>();
                var cappedBySlot = new Dictionary<int, bool>();
                foreach (var v in pointsInWindow)
                {
                    var primary = v.Default ?? v.Average ?? v.Maximum;
                    if (!primary.HasValue)
                    {
                        continue;
                    }

                    var closest = maxSeries
                        .Select(x => new { x, dist = Math.Abs((x.TimeStamp - v.TimeStamp).TotalMinutes) })
                        .Where(x => x.dist <= MarginMinutes)
                        .OrderBy(x => x.dist)
                        .FirstOrDefault();
                    double? maxAtPoint = closest != null ? (closest.x.Maximum ?? closest.x.Average) : null;
                    var pointCapped = maxAtPoint.HasValue && maxAtPoint.Value > 0 && primary.Value >= CapThreshold * maxAtPoint.Value;

                    if (pointCapped)
                    {
                        windowCapped = true;
                    }

                    if (!windowMax.HasValue || primary.Value > windowMax.Value)
                    {
                        windowMax = primary.Value;
                    }

                    var minutesFromMidnight = (v.TimeStamp - date).TotalMinutes;
                    var slotIndex = (int)(minutesFromMidnight / slotMinutes);
                    if (slotIndex < 0)
                    {
                        slotIndex = 0;
                    }
                    else if (slotIndex >= slotsPerDay)
                    {
                        slotIndex = slotsPerDay - 1;
                    }

                    if (!valueBySlot.TryGetValue(slotIndex, out var existing) || primary.Value > existing)
                    {
                        valueBySlot[slotIndex] = primary.Value;
                    }

                    if (pointCapped)
                    {
                        cappedBySlot[slotIndex] = true;
                    }
                }

                if (windowMax.HasValue)
                {
                    results.Add(new DailyWindowResult
                    {
                        DayOfWeek = date.DayOfWeek,
                        WindowMax = windowMax.Value,
                        AnyCapped = windowCapped,
                        ValueByHour = valueBySlot,
                        CappedByHour = cappedBySlot
                    });
                }
            }

            return results;
        }

        private static double CalculateAffinity(DayOfWeek targetDay, DayOfWeek sampleDay, double sameDayFactor, double weekdayFactor, double weekendFactor)
        {
            if (targetDay == sampleDay)
            {
                return sameDayFactor;
            }

            if (IsWeekday(targetDay) && IsWeekday(sampleDay))
            {
                return weekdayFactor;
            }

            if (!IsWeekday(targetDay) && !IsWeekday(sampleDay))
            {
                return weekendFactor;
            }

            return 0;
        }

        private static bool IsWeekday(DayOfWeek day)
        {
            return day >= DayOfWeek.Monday && day <= DayOfWeek.Friday;
        }

        private static int ResolveSlotMinutes(int? value)
        {
            var minutes = value ?? MaxSlotMinutes;
            return Math.Clamp(minutes, MinSlotMinutes, MaxSlotMinutes);
        }

        private static int ResolveSlotMinutes(int value)
        {
            return Math.Clamp(value, MinSlotMinutes, MaxSlotMinutes);
        }

        private static string ResolveMaxMetricName(Metric metric, ScalingConfiguration setting)
        {
            if (string.IsNullOrEmpty(metric.ForecastMetricMax))
            {
                return null;
            }

            if (setting?.Metrics != null && setting.Metrics.TryGetValue(metric.ForecastMetricMax, out var maxMetric))
            {
                return maxMetric.Name;
            }

            return metric.ForecastMetricMax;
        }

        private sealed class DailyWindowResult
        {
            public DayOfWeek DayOfWeek { get; set; }

            public double WindowMax { get; set; }

            public bool AnyCapped { get; set; }

            public Dictionary<int, double> ValueByHour { get; set; } = new Dictionary<int, double>();

            public Dictionary<int, bool> CappedByHour { get; set; } = new Dictionary<int, bool>();
        }

        /// <summary>Summary of how the forecast was built, for reliability debug output.</summary>
        private sealed class ForecastBuildInfo
        {
            public ForecastBuildInfo(
                DateTimeOffset startDateUtc,
                DateTimeOffset endDateUtc,
                int daysWithDataCount,
                Dictionary<DayOfWeek, int> samplesPerDayOfWeek)
            {
                this.StartDateUtc = startDateUtc;
                this.EndDateUtc = endDateUtc;
                this.DaysWithDataCount = daysWithDataCount;
                this.SamplesPerDayOfWeek = samplesPerDayOfWeek;
                this.TotalWeeksAnalyzed = (endDateUtc - startDateUtc).TotalDays / 7.0;
            }

            public DateTimeOffset StartDateUtc { get; }

            public DateTimeOffset EndDateUtc { get; }

            public int DaysWithDataCount { get; }

            public double TotalWeeksAnalyzed { get; }

            public Dictionary<DayOfWeek, int> SamplesPerDayOfWeek { get; }
        }
    }
}
