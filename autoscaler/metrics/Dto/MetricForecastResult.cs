using Azure.Monitor.Query.Models;

namespace poolautoscaler.metrics.Dto
{
    /// <summary>
    /// Full forecast result: value and capped flag per (day of week, hour). Cacheable; use <see cref="MetricForecastService.GetCurrentForecastValue"/> to get the value to apply for a given time.
    /// </summary>
    public class MetricForecastResult
    {
        /// <summary>Forecast value per day of week and hour (UTC). ValueByDayAndHour[day][hour] = projected value.</summary>
        public Dictionary<DayOfWeek, Dictionary<int, double>> ValueByDayAndHour { get; set; } = new Dictionary<DayOfWeek, Dictionary<int, double>>();

        /// <summary>Whether the forecast for that (day, hour) was constrained by the max metric (potentially underestimated).</summary>
        public Dictionary<DayOfWeek, Dictionary<int, bool>> CappedByDayAndHour { get; set; } = new Dictionary<DayOfWeek, Dictionary<int, bool>>();

        /// <summary>When this forecast should be considered stale and recomputed.</summary>
        public DateTime ExpiresAtUtc { get; set; }

        /// <summary>Metric id (for logging).</summary>
        public string MetricId { get; set; }

        /// <summary>Aggregations used (for building metric result).</summary>
        public IList<MetricAggregationType> ExecutedAggregations { get; set; }
    }
}
