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
        /// Granularity of forecast analysis windows (e.g. "60m"). Defaults to "60m".
        /// </summary>
        public string ForecastGranularity { get; set; } = "60m";

        /// <summary>
        /// Required when ForecastEnable is true. Metric id or Azure metric name that represents the maximum available value (ceiling).
        /// We must know what we are scaling against; when the main metric value is at or near this ceiling, the observed value may be capped
        /// and the forecast is flagged as potentially underestimated. Defaults to "max_available_metric".
        /// </summary>
        public string ForecastMetricMax { get; set; } = "max_available_metric";

        /// <summary>Parsed forecast time range.</summary>
        public TimeSpan? ForecastTimeRangeParsed { get; set; }

        /// <summary>Parsed forecast granularity.</summary>
        public TimeSpan? ForecastGranularityParsed { get; set; }

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
    }
}
