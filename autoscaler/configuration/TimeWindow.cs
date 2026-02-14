namespace poolautoscaler.configuration
{
    /// <summary>When a scaling config is active (timezone, days, months, time range).</summary>
    public class TimeWindow
    {
        /// <summary>IANA time zone (e.g. "UTC").</summary>
        public string TimeZone { get; set; } = "UTC";

        /// <summary>Parsed time zone.</summary>
        public TimeZoneInfo TimeZoneParsed { get; set; }

        /// <summary>Days filter (e.g. "Weekday", "All", or comma-separated).</summary>
        public string Days { get; set; }

        /// <summary>Months filter (e.g. "All" or month names).</summary>
        public string Months { get; set; }

        /// <summary>Start time string (e.g. "09:00").</summary>
        public string StartTime { get; set; }

        /// <summary>Parsed start time.</summary>
        public TimeSpan? StartTimeParsed { get; set; }

        /// <summary>End time string.</summary>
        public string EndTime { get; set; }

        /// <summary>Parsed end time.</summary>
        public TimeSpan? EndTimeParsed { get; set; }
    }
}
