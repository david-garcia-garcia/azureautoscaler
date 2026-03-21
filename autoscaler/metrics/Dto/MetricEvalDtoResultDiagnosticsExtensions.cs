using Microsoft.Extensions.Logging;

namespace poolautoscaler.metrics.Dto
{
    /// <summary>Extensions for emitting <see cref="MetricEvaluationDiagnostics"/>.</summary>
    public static class MetricEvalDtoResultDiagnosticsExtensions
    {
        /// <summary>
        /// Logs each diagnostic line at <paramref name="level"/> when <see cref="MetricEvalDtoResult.Diagnostics"/> is present.
        /// </summary>
        /// <param name="result">The metric evaluation result.</param>
        /// <param name="logger">The logger.</param>
        /// <param name="level">The log level.</param>
        public static void EmitDiagnostics(this MetricEvalDtoResult result, ILogger logger, LogLevel level)
        {
            if (result?.Diagnostics == null || !result.Diagnostics.HasContent)
            {
                return;
            }

            foreach (var line in result.Diagnostics.Lines)
            {
                logger.Log(level, "{DiagnosticLine}", line);
            }
        }
    }
}
