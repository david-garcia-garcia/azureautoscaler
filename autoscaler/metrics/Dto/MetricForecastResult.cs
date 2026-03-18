using Azure.Monitor.Query.Models;

namespace poolautoscaler.metrics.Dto
{
    /// <summary>
    /// Full forecast result: value and capped flag per (day of week, slot). Cacheable; use <see cref="MetricForecastService.GetCurrentForecastValue"/> to get the value to apply for a given time.
    /// Slot index = minutes from midnight (UTC) / <see cref="SlotMinutes"/> (0-based).
    /// </summary>
    public class MetricForecastResult
    {
        /// <summary>Slot interval in minutes (15, 30, or 60). Slots per day = 1440 / SlotMinutes.</summary>
        public int SlotMinutes { get; set; } = 60;

        /// <summary>Forecast value per day of week and slot index. ValueByDayAndHour[day][slotIndex] = projected value.</summary>
        public Dictionary<DayOfWeek, Dictionary<int, double>> ValueByDayAndHour { get; set; } = new Dictionary<DayOfWeek, Dictionary<int, double>>();

        /// <summary>Whether the forecast for that (day, slot) was built from history that was constrained by the max metric at some points. Those points are corrected (e.g. multiplied by a factor) and used in the baseline; this flag is for logging/information only and does not invalidate the forecast.</summary>
        public Dictionary<DayOfWeek, Dictionary<int, bool>> CappedByDayAndHour { get; set; } = new Dictionary<DayOfWeek, Dictionary<int, bool>>();

        /// <summary>Snapped forecast (Anchors or Snap mode). When set, scaling uses this instead of ValueByDayAndHour. Same shape (day, slot) -> value.</summary>
        public Dictionary<DayOfWeek, Dictionary<int, double>> SnappedValueByDayAndHour { get; set; }

        /// <summary>Forecast mode name for logging (e.g. "Anchors", "Snap"). Set when SnappedValueByDayAndHour is populated.</summary>
        public string SnappedModeName { get; set; }

        /// <summary>Percentile (0-100) used for the snapped forecast. Set when SnappedValueByDayAndHour is populated.</summary>
        public int? SnappedPercentile { get; set; }

        /// <summary>Mode-specific parameters for logging (e.g. "anchors 05:00, 20:00", "3 windows"). Set when SnappedValueByDayAndHour is populated.</summary>
        public string SnappedParameterSummary { get; set; }

        /// <summary>When this forecast should be considered stale and recomputed.</summary>
        public DateTime ExpiresAtUtc { get; set; }

        /// <summary>Metric id (for logging).</summary>
        public string MetricId { get; set; }

        /// <summary>Aggregations used (for building metric result).</summary>
        public IList<MetricAggregationType> ExecutedAggregations { get; set; }
    }
}
