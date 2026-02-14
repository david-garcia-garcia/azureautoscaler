using poolautoscaler.metrics.Dto;

namespace poolautoscaler.strategies.Dto
{
    /// <summary>Metric results and dimension step helpers for rule evaluation.</summary>
    public class MetricEvalDto
    {
        /// <summary>Metric results keyed by metric ID.</summary>
        public Dictionary<string, MetricEvalDtoResult> Metrics = new Dictionary<string, MetricEvalDtoResult>();

        /// <summary>Steps to next dimension value (e.g. next tier).</summary>
        public Func<long, string> _NextDimensionValue { get; set; }

        /// <summary>Steps to previous dimension value.</summary>
        public Func<long, string> _PreviousDimensionValue { get; set; }

        /// <summary>Returns next dimension value after step.</summary>
        /// <param name="step">The step index.</param>
        /// <returns>Next dimension value.</returns>
        public string NextDimensionValue(long step)
        {
            return this._NextDimensionValue(step);
        }

        /// <summary>Returns previous dimension value after step.</summary>
        /// <param name="step">The step index.</param>
        /// <returns>Previous dimension value.</returns>
        public string PreviousDimensionValue(long step)
        {
            return this._PreviousDimensionValue(step);
        }
    }
}
