namespace poolautoscaler.resources.MySqlFlexibleServer.Dto
{
    /// <summary>Capacity forecast per day of week with expiration.</summary>
    public class CapacityForecast
    {
        /// <summary>Gets or sets the forecast values per day of week.</summary>
        public Dictionary<DayOfWeek, double> Forecast { get; set; }

        /// <summary>Gets or sets the forecast string representation per day of week.</summary>
        public Dictionary<DayOfWeek, string> ForecastString { get; set; }

        /// <summary>Gets or sets when this forecast expires.</summary>
        public DateTime ExpiresAt { get; set; }
    }
}
