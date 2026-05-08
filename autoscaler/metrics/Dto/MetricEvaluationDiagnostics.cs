namespace poolautoscaler.metrics.Dto
{
    /// <summary>
    /// Optional structured diagnostic lines for a metric evaluation (e.g. forecast tables).
    /// Main lines are emitted at <see cref="Microsoft.Extensions.Logging.LogLevel.Debug"/> (or caller-chosen level);
    /// <see cref="TraceLines"/> are always emitted at <see cref="Microsoft.Extensions.Logging.LogLevel.Trace"/>.
    /// </summary>
    public sealed class MetricEvaluationDiagnostics
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MetricEvaluationDiagnostics"/> class.
        /// </summary>
        /// <param name="lines">Diagnostic lines for the primary level; null or empty is allowed if only trace detail exists.</param>
        /// <param name="traceLines">Fine-grained baseline contributor lines etc.; emitted at trace only.</param>
        public MetricEvaluationDiagnostics(IReadOnlyList<string>? lines, IReadOnlyList<string>? traceLines = null)
        {
            this.Lines = lines == null || lines.Count == 0
                ? Array.Empty<string>()
                : lines.ToArray();

            this.TraceLines = traceLines == null || traceLines.Count == 0
                ? Array.Empty<string>()
                : traceLines.ToArray();
        }

        /// <summary>Ordered lines to log at the caller-requested level.</summary>
        public IReadOnlyList<string> Lines { get; }

        /// <summary>Detailed lines emitted only at trace (e.g. per-cell baseline pipeline breakdown).</summary>
        public IReadOnlyList<string> TraceLines { get; }

        /// <summary>True when there is at least one main or trace line to emit.</summary>
        public bool HasContent => this.Lines.Count > 0 || this.TraceLines.Count > 0;
    }
}
