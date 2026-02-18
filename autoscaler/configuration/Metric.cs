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
    }
}
