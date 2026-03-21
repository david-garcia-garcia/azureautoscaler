namespace poolautoscaler.metrics.Dto
{
    /// <summary>
    /// Optional structured diagnostic lines for a metric evaluation (e.g. forecast tables).
    /// Emitted on demand at Debug during evaluation and at Information when a scale applies.
    /// </summary>
    public sealed class MetricEvaluationDiagnostics
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MetricEvaluationDiagnostics"/> class.
        /// </summary>
        /// <param name="lines">Diagnostic lines; null or empty yields no emission.</param>
        public MetricEvaluationDiagnostics(IReadOnlyList<string>? lines)
        {
            this.Lines = lines == null || lines.Count == 0
                ? Array.Empty<string>()
                : lines.ToArray();
        }

        /// <summary>Ordered lines to log (tables, headers, summaries).</summary>
        public IReadOnlyList<string> Lines { get; }

        /// <summary>True when there is at least one line to emit.</summary>
        public bool HasContent => this.Lines.Count > 0;
    }
}
