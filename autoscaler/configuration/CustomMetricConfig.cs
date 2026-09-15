using poolautoscaler.metrics.Dto;

namespace poolautoscaler.configuration
{
    /// <summary>Configuration for a single custom metric to push to Azure Monitor.</summary>
    public class CustomMetricConfig
    {
        /// <summary>Metric name (e.g. node_count, core_count). Required for DataExpression rows; unused for Query rows.</summary>
        public string? Name { get; set; }

        /// <summary>
        /// Optional namespace for the metric in Azure Monitor (overrides global default).
        /// If not set, the global default (Configuration.CustomMetricsNamespace) is used,
        /// otherwise "Custom Autoscaler".
        /// </summary>
        public string? Namespace { get; set; }

        /// <summary>
        /// Azure resource ID where the metric will be published.
        /// Supports placeholders like ${virtualMachineScaleSetId}, ${subscriptionId}.
        /// </summary>
        public string ResourceId { get; set; }

        /// <summary>
        /// Expression evaluated to produce the metric value.
        /// Parameter "data" exposes Resource, ExistingState, ResourceParts, Helpers.
        /// Example: "(data) => data.Resource.Sku.Capacity".
        /// Example: "(data) => data.ExistingState.CoreCount".
        /// Exclusive with <see cref="Query"/>.
        /// </summary>
        public string? DataExpression { get; set; }

        /// <summary>
        /// SQL text executed once per due cycle. First-row numeric columns become Azure Monitor metrics named after those columns.
        /// Exclusive with <see cref="DataExpression"/>. Allowed only on the four SQL resource types.
        /// </summary>
        public string? Query { get; set; }

        /// <summary>SQL command timeout (e.g. "30s"). Defaults to 30 seconds when omitted.</summary>
        public string? QueryTimeout { get; set; }

        /// <summary>Parsed SQL command timeout. Defaults to 30 seconds.</summary>
        public TimeSpan QueryTimeoutParsed { get; set; }

        /// <summary>Push interval (e.g. "1m", "5m").</summary>
        public string Frequency { get; set; }

        /// <summary>Parsed push interval.</summary>
        public TimeSpan FrequencyParsed { get; set; }

        /// <summary>Compiled expression returning a numeric value. Null on Query rows.</summary>
        public Func<CustomMetricDataContext, double>? DataExpressionDelegate { get; set; }
    }
}
