using poolautoscaler.configuration;
using poolautoscaler.metrics.Dto;

namespace poolautoscaler.metrics.ForecastModes
{
    /// <summary>Raw mode: no transformation; baseline forecast is used as-is. TryApply always returns false so no Snapped* fields are set.</summary>
    public sealed class ForecastModeRaw : IForecastModeStrategy
    {
        /// <inheritdoc />
        public string ModeName => "Raw";

        /// <inheritdoc />
        public bool TryApply(MetricForecastResult result, Metric metric)
        {
            return false;
        }
    }
}
