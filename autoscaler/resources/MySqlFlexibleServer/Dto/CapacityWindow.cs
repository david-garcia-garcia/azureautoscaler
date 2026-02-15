namespace poolautoscaler.resources.MySqlFlexibleServer.Dto
{
    /// <summary>Capacity window for a day with samples and coverage.</summary>
    public class CapacityWindow
    {
        /// <summary>Gets or sets the maximum recommended capacity.</summary>
        public double? MaxRecommendedCapacity { get; set; }

        /// <summary>Gets or sets the maximum effective capacity.</summary>
        public double? MaxEffectiveCapacity { get; set; }

        /// <summary>Gets or sets the day this window covers.</summary>
        public DateTimeOffset Day { get; set; }

        /// <summary>Gets or sets the capacity samples in this window.</summary>
        public List<CapacitySample> CapacitySamples { get; set; }

        /// <summary>Gets or sets the window coverage percentage.</summary>
        public int WindowCoveragePercent { get; set; }

        /// <summary>Gets or sets the window start timestamp.</summary>
        public DateTimeOffset StartTimestmap { get; set; }

        /// <summary>Gets or sets the window duration.</summary>
        public TimeSpan Duration { get; set; }

        /// <summary>Gets or sets the affinity value.</summary>
        public double Affinity { get; set; }
    }
}
