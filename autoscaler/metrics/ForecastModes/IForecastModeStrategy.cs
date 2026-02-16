using poolautoscaler.configuration;
using poolautoscaler.metrics.Dto;

namespace poolautoscaler.metrics.ForecastModes
{
    /// <summary>
    /// Strategy that transforms a baseline forecast (ValueByDayAndHour) into a snapped forecast (SnappedValueByDayAndHour)
    /// for a specific ForecastMode. Does not modify the baseline.
    /// </summary>
    public interface IForecastModeStrategy
    {
        /// <summary>Mode name this strategy handles (e.g. "Anchors", "AnchorWindow", "Snap", "Raw"). Case-insensitive match.</summary>
        string ModeName { get; }

        /// <summary>
        /// If the metric's ForecastMode matches this strategy, applies the mode and sets result.SnappedValueByDayAndHour,
        /// SnappedModeName, SnappedPercentile, SnappedParameterSummary. Does not modify result.ValueByDayAndHour. Raw returns false.
        /// </summary>
        /// <param name="result">Forecast result (baseline in ValueByDayAndHour; snapped output written to Snapped*).</param>
        /// <param name="metric">Metric configuration (ForecastMode and mode-specific settings).</param>
        /// <returns>True if the strategy applied (mode matched and config was valid); false otherwise.</returns>
        bool TryApply(MetricForecastResult result, Metric metric);
    }
}
