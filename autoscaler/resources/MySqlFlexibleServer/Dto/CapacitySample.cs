namespace poolautoscaler.resources.MySqlFlexibleServer.Dto
{
    public class CapacitySample
    {
        public double? UsedMillicores { get; set; }

        public double? RecommendedMillicores { get; set; }

        public double? EffectiveAverageUsage { get; set; }

        public double? EffectiveValue { get; set; }

        public double? RecommendedValue { get; set; }

        public DateTimeOffset Timestamp { get; set; }
    }
}
