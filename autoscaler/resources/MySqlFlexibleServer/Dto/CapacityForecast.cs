namespace poolautoscaler.resources.MySqlFlexibleServer.Dto
{
    public class CapacityForecast
    {
        public Dictionary<DayOfWeek, double> Forecast { get; set; }

        public Dictionary<DayOfWeek, string> ForecastString { get; set; }

        public DateTime ExpiresAt { get; set; }
    }
}
