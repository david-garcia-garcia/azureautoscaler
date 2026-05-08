using Azure.Monitor.Query.Models;

namespace poolautoscaler.metrics.Dto
{
    /// <summary>
    /// Full forecast result: value and capped flag per (day of week, slot). Cacheable; use <see cref="MetricForecastService.GetCurrentForecastValue"/> to get the value to apply for a given time.
    /// Slot index = minutes from midnight (UTC) / <see cref="SlotMinutes"/> (0-based).
    /// </summary>
    public class MetricForecastResult
    {
        /// <summary>Slot interval in minutes (15, 30, or 60). Slots per day = 1440 / SlotMinutes.</summary>
        public int SlotMinutes { get; set; } = 60;

        /// <summary>Forecast value per day of week and slot index. ValueByDayAndHour[day][slotIndex] = projected value.</summary>
        public Dictionary<DayOfWeek, Dictionary<int, double>> ValueByDayAndHour { get; set; } = new Dictionary<DayOfWeek, Dictionary<int, double>>();

        /// <summary>Whether the forecast for that (day, slot) was built from history that was constrained by the max metric at some points. Those points are corrected (e.g. multiplied by a factor) and used in the baseline; this flag is for logging/information only and does not invalidate the forecast.</summary>
        public Dictionary<DayOfWeek, Dictionary<int, bool>> CappedByDayAndHour { get; set; } = new Dictionary<DayOfWeek, Dictionary<int, bool>>();

        /// <summary>
        /// Diagnostic only: per baseline cell, affinity-matched historical daily samples (ordered by calendar date UTC)
        /// whose slot values feed <see cref="ValueByDayAndHour"/> via the configured baseline aggregation mode.
        /// </summary>
        public Dictionary<DayOfWeek, Dictionary<int, List<ForecastBaselineContributor>>>? BaselineContributorsByDayAndSlot { get; set; }

        /// <summary>Diagnostic: metric's <c>ForecastBaselineAggregation</c> as passed in (null = unset).</summary>
        public string? BaselineAggregationConfigured { get; set; }

        /// <summary>Diagnostic: normalized mode actually applied (<c>Max</c>, <c>Mean</c>, or <c>WeightedMean</c>).</summary>
        public string BaselineAggregationEffective { get; set; } = "Max";

        /// <summary>Diagnostic: multiplier applied to each baseline cell after aggregation (<c>ForecastBaselineBoostFactor</c>; 1.0 when disabled).</summary>
        public double BaselineBoostFactorApplied { get; set; } = 1.0;

        /// <summary>Diagnostic: effective neighbour weight for temporal baseline smoothing (0 when disabled; may be clamped from config).</summary>
        public double BaselineSmoothingNeighbourWeightApplied { get; set; }

        /// <summary>Diagnostic τ (days) used for WeightedMean recency decay when mode is WeightedMean; otherwise null.</summary>
        public double? BaselineWeightedMeanHalfLifeDays { get; set; }

        /// <summary>Capped-vs-max ratio threshold persisted for RTL traces (<c>ForecastCappedCorrectionThreshold</c>).</summary>
        public double BaselineCapDetectThresholdApplied { get; set; }

        /// <summary>Capped correction multiplier stored for RTL traces (ForecastCappedCorrectionFactor).</summary>
        public double BaselineCapCorrectionFactorApplied { get; set; } = 1.0;

        /// <summary>Optional per-cell cross-day/smooth/boost pipeline for trace RTL lines.</summary>
        public Dictionary<DayOfWeek, Dictionary<int, ForecastBaselineCellDiagnostics>>? BaselineCellDiagnosticsByDayAndSlot { get; set; }

        /// <summary>Snapped forecast (Anchors or Snap mode). When set, scaling uses this instead of ValueByDayAndHour. Same shape (day, slot) -> value.</summary>
        public Dictionary<DayOfWeek, Dictionary<int, double>> SnappedValueByDayAndHour { get; set; }

        /// <summary>Forecast mode name for logging (e.g. "Anchors", "Snap"). Set when SnappedValueByDayAndHour is populated.</summary>
        public string SnappedModeName { get; set; }

        /// <summary>Percentile (0-100) used for the snapped forecast. Set when SnappedValueByDayAndHour is populated.</summary>
        public int? SnappedPercentile { get; set; }

        /// <summary>Mode-specific parameters for logging (e.g. "anchors 05:00, 20:00", "3 windows"). Set when SnappedValueByDayAndHour is populated.</summary>
        public string SnappedParameterSummary { get; set; }

        /// <summary>When this forecast should be considered stale and recomputed.</summary>
        public DateTime ExpiresAtUtc { get; set; }

        /// <summary>Metric id (for logging).</summary>
        public string MetricId { get; set; }

        /// <summary>Aggregations used (for building metric result).</summary>
        public IList<MetricAggregationType> ExecutedAggregations { get; set; }

        /// <summary>Pre-rendered diagnostic lines: baseline weekly table when the full forecast is computed, then optional snapped section after snap is applied.</summary>
        public IReadOnlyList<string>? DiagnosticLines { get; set; }

        /// <summary>Optional high-volume baseline detail (per-cell pipeline lines); emitted at trace only via <see cref="MetricEvaluationDiagnostics.TraceLines"/>.</summary>
        public IReadOnlyList<string>? DiagnosticTraceLines { get; set; }

        /// <summary>Structured summary of how the baseline was built (history window, sample counts). Populated with <see cref="DiagnosticLines"/> when the full forecast is computed.</summary>
        public ForecastBuildInfo? BuildInfo { get; set; }
    }
}
