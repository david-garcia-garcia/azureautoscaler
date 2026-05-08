using Azure.Monitor.Query.Models;
using poolautoscaler.metrics.Dto;

namespace poolautoscaler.configuration
{
    /// <summary>Metric definition (name, resource, aggregations, optional transform).</summary>
    public class Metric
    {
        /// <summary>Metric identifier.</summary>
        public string Id { get; set; }

        /// <summary>Metric name (e.g. Azure Monitor metric).</summary>
        public string Name { get; set; }

        /// <summary>
        /// Optional Azure Monitor metric namespace to query this metric from.
        /// Required for custom metrics (for example, "Custom Autoscaler") when the same metric name doesn't exist in the default namespace.
        /// </summary>
        public string? Namespace { get; set; }

        /// <summary>Resource ID to query.</summary>
        public string ResourceId { get; set; }

        /// <summary>Split dimension name for multi-resource metrics.</summary>
        public string SplitName { get; set; }

        /// <summary>Split value filter.</summary>
        public string SplitValue { get; set; }

        /// <summary>Time window expression.</summary>
        public string Window { get; set; }

        /// <summary>Time grain (e.g. PT1M).</summary>
        public string TimeGrain { get; set; }

        /// <summary>Aggregation names (e.g. Average, Maximum).</summary>
        public List<string> Aggregations { get; set; }

        /// <summary>Parsed aggregation types.</summary>
        public List<MetricAggregationType> ParsedAggregations = null;

        /// <summary>Optional transform applied to metric value.</summary>
        public Func<MetricEvalDtoResultValue, MetricEvalDtoResultValue> TransformExpression { get; set; }

        /// <summary>Transform expression string.</summary>
        public string Transform { get; set; }

        /// <summary>
        /// When true, allows the metric to fail to load without throwing an exception.
        /// Instead, a debug message will be logged. Defaults to false.
        /// Useful when a metric may not be available for certain resource configurations (e.g., dtu_consumption_percent is not available for VCore model SQL databases).
        /// </summary>
        public bool AllowFail { get; set; } = false;

        /// <summary>
        /// Minimum valid value for this metric. If the metric returns a value below this, it will be considered broken/invalid.
        /// Useful for detecting broken metrics (e.g., storage_used should never be 0 for a pool with data).
        /// </summary>
        public double? ValidValueMin { get; set; }

        /// <summary>
        /// Maximum valid value for this metric. If the metric returns a value above this, it will be considered broken/invalid.
        /// </summary>
        public double? ValidValueMax { get; set; }

        // -------------------------------------------------------------------------
        // Forecast (optional): when enabled, a derived metric Id_forecast is produced.
        // -------------------------------------------------------------------------

        /// <summary>
        /// When true, a forecast value is computed from history and added as a separate metric (Id + "_forecast").
        /// Defaults to false.
        /// </summary>
        public bool ForecastEnable { get; set; } = false;

        /// <summary>
        /// Time range of history to use for forecasting (e.g. "15d", "60m"). Defaults to "15d".
        /// </summary>
        public string ForecastTimeRange { get; set; } = "15d";

        /// <summary>
        /// Time grain in minutes when fetching metric history for forecast (e.g. 60). Must be &lt;= ForecastSlotMinutes. Defaults to 60.
        /// </summary>
        public int? ForecastMetricsGranularityMinutes { get; set; }

        /// <summary>
        /// Required when ForecastEnable is true. Metric id or Azure metric name that represents the maximum available value (ceiling).
        /// We must know what we are scaling against; when the main metric value is at or near this ceiling, the observed value may be capped
        /// and the forecast is flagged as potentially underestimated. Defaults to "max_available_metric".
        /// </summary>
        public string ForecastMetricMax { get; set; } = "max_available_metric";

        /// <summary>
        /// Forecast slot interval in minutes (15, 30, or 60). Each day is split into slots of this duration; forecast value is per (day, slot). Defaults to 60.
        /// Minimum 15, maximum 60.
        /// </summary>
        public int? ForecastSlotMinutes { get; set; }

        /// <summary>Parsed forecast time range.</summary>
        public TimeSpan? ForecastTimeRangeParsed { get; set; }

        /// <summary>
        /// Affinity weight when target day and sample day are the same (used when aggregating history by day of week). Defaults to 1.0.
        /// </summary>
        public double? ForecastAffinitySameDayFactor { get; set; }

        /// <summary>
        /// Affinity weight when both target and sample are weekdays but different days. Defaults to 0.3.
        /// </summary>
        public double? ForecastAffinityWeekdayFactor { get; set; }

        /// <summary>
        /// Affinity weight when both target and sample are weekend days but different days. Defaults to 0.3.
        /// </summary>
        public double? ForecastAffinityWeekendFactor { get; set; }

        /// <summary>
        /// How to combine historical same-weekday slot values into one baseline cell: <c>Max</c>, <c>Mean</c>, or <c>WeightedMean</c>.
        /// When null or unset, behaves as <c>Max</c> (backward compatible).
        /// </summary>
        /// <remarks>
        /// Together with baseline tuning: see <see cref="ForecastBaselineDecayDays"/>,
        /// <see cref="ForecastBaselineSmoothingNeighbourWeight"/>, <see cref="ForecastBaselineBoostFactor"/>.
        /// Order of application: aggregation → temporal smoothing → boost → snap/anchor (see project docs).
        /// </remarks>
        public string? ForecastBaselineAggregation { get; set; }

        /// <summary>
        /// Exponential decay time constant τ (days) for <c>WeightedMean</c>: contributor weight ∝ exp(−daysAgo / τ).
        /// At age τ the relative factor is exp(−1) (~37%); the age where relative weight is halved is about τ × ln(2).
        /// When null or unset, defaults to 14.0 days. Values less than or equal to zero are treated as 14.0.
        /// </summary>
        /// <remarks>
        /// Ignored unless aggregation is <c>WeightedMean</c>. See <c>docs/forecast-baseline-parameters.md</c>.
        /// See also <see cref="ForecastBaselineAggregation"/>,
        /// <see cref="ForecastBaselineSmoothingNeighbourWeight"/>, <see cref="ForecastBaselineBoostFactor"/>.
        /// </remarks>
        public double? ForecastBaselineDecayDays { get; set; }

        /// <summary>
        /// Weight (0–0.49) for each <em>existing</em> time-adjacent slot when blending the cross-day aggregate for the same weekday.
        /// Upward-only: a slot is replaced only if the blend is higher than the raw aggregate. Values null, ≤ 0, or NaN disable smoothing.
        /// Values ≥ 0.5 are clamped to 0.49. Runs after aggregation and before <see cref="ForecastBaselineBoostFactor"/>.
        /// </summary>
        /// <remarks>
        /// See <see cref="ForecastBaselineAggregation"/>, <see cref="ForecastBaselineDecayDays"/>, <see cref="ForecastBaselineBoostFactor"/>
        /// and <c>docs/forecast-baseline-parameters.md</c>.
        /// </remarks>
        public double? ForecastBaselineSmoothingNeighbourWeight { get; set; }

        /// <summary>
        /// Multiplier applied to each baseline cell <em>after</em> cross-day aggregation and temporal smoothing (before snap/anchor modes).
        /// Example: 1.20 increases the aggregate by 20%. When null, unset, NaN, infinity, or ≤ 0, defaults to 1.0 (no change).
        /// </summary>
        /// <remarks>
        /// See <see cref="ForecastBaselineAggregation"/>, <see cref="ForecastBaselineDecayDays"/>,
        /// <see cref="ForecastBaselineSmoothingNeighbourWeight"/>.
        /// </remarks>
        public double? ForecastBaselineBoostFactor { get; set; }

        /// <summary>
        /// Threshold ratio (0-1) to consider a point potentially capped when comparing main usage to ForecastMetricMax.
        /// Example: 0.95 means usage >= 95% of available capacity is considered capped. Defaults to 0.95.
        /// </summary>
        public double? ForecastCappedCorrectionThreshold { get; set; }

        /// <summary>
        /// Multiplicative factor applied to capped points to compensate likely under-observation.
        /// Example: 1.15 means a capped value of 50 is treated as 57.5 for baseline aggregation.
        /// Values less than 1 are clamped to 1 (no correction). Defaults to 1.2.
        /// </summary>
        public double? ForecastCappedCorrectionFactor { get; set; }

        // -------------------------------------------------------------------------
        // Forecast snap (optional): transform baseline forecast for scaling decisions.
        // -------------------------------------------------------------------------

        /// <summary>Forecast mode: "Raw" (baseline only), "Anchors" (fixed intervals by hour), "AnchorWindow" (windows with optimal change moment), or "Snap" (rolling window). Default/unset = Raw.</summary>
        public string ForecastMode { get; set; }

        /// <summary>Anchor mode: hours that define interval boundaries (e.g. "05:00", "20:00"). Scaling permitted within intervals; value per interval is percentile of baseline in that interval.</summary>
        public List<string> ForecastSnapAnchorHours { get; set; }

        /// <summary>AnchorWindow mode: time windows (e.g. "20:00-23:00", "03:00-07:00"). For each window, the system chooses when to switch to the percentile value so as to minimize total resource usage while complying with the minimum percentile.</summary>
        public List<string> ForecastAnchorWindows { get; set; }

        /// <summary>Percentile (0-100) used by both snap modes. E.g. 95 = use 95th percentile of values in the window.</summary>
        public int? ForecastSnapPercentile { get; set; }

        /// <summary>StepSnap mode: number of consecutive slots (current + future) to consider. E.g. 3 = current slot plus next 2.</summary>
        public int? ForecastSnapStepWindows { get; set; }
    }
}
