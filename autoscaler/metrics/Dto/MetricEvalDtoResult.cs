using Azure.Monitor.Query.Models;

namespace poolautoscaler.metrics.Dto
{
    /// <summary>Result of a metric query (values, validity, aggregations).</summary>
    public class MetricEvalDtoResult
    {
        /// <summary>
        /// For metrics that provide a range of values.
        /// </summary>
        public List<MetricEvalDtoResultValue> Values = new List<MetricEvalDtoResultValue>();

        /// <summary>
        /// Indicates whether this metric data is valid and can be trusted for scaling decisions.
        /// If false, the metric is considered broken/unreliable and should not be used.
        /// </summary>
        public bool Valid { get; set; } = true;

        /// <summary>
        /// Optional message explaining why the metric is invalid.
        /// </summary>
        public string InvalidReason { get; set; }

        /// <summary>
        /// The aggregations that were actually executed when retrieving this metric.
        /// This is the effective list after applying defaults (defaults to Average if not specified).
        /// </summary>
        public IList<MetricAggregationType> ExecutedAggregations { get; set; }

        /// <summary>
        /// The primary aggregation type used for the Default property in values.
        /// This is the first aggregation from ExecutedAggregations.
        /// </summary>
        public MetricAggregationType? PrimaryAggregation =>
            this.ExecutedAggregations != null && this.ExecutedAggregations.Any()
                ? this.ExecutedAggregations.First()
                : null;

        /// <summary>
        /// Optional diagnostics (e.g. full forecast tables) emitted at Debug during evaluation and at Information when a scale applies.
        /// </summary>
        public MetricEvaluationDiagnostics? Diagnostics { get; set; }
    }
}
