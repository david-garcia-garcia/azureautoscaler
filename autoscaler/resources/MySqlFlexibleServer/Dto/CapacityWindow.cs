namespace poolautoscaler.resources.MySqlFlexibleServer.Dto
{
    public class CapacityWindow
    {
        public double? MaxRecommendedCapacity { get; set; }

        public double? MaxEffectiveCapacity { get; set; }

        public DateTimeOffset Day { get; set; }

        public List<CapacitySample> CapacitySamples { get; set; }

        public int WindowCoveragePercent { get; set; }

        public DateTimeOffset StartTimestmap { get; set; }

        public TimeSpan Duration { get; set; }

        public double Affinity { get; set; }
    }
}
