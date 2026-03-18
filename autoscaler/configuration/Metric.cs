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
