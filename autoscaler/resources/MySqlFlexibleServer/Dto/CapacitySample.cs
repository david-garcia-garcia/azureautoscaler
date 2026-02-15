namespace poolautoscaler.resources.MySqlFlexibleServer.Dto
{
    /// <summary>Single capacity sample for a point in time.</summary>
    public class CapacitySample
    {
        /// <summary>Gets or sets the used millicores.</summary>
        public double? UsedMillicores { get; set; }

        /// <summary>Gets or sets the recommended millicores.</summary>
        public double? RecommendedMillicores { get; set; }

        /// <summary>Gets or sets the effective average usage.</summary>
        public double? EffectiveAverageUsage { get; set; }

        /// <summary>Gets or sets the effective value.</summary>
        public double? EffectiveValue { get; set; }

        /// <summary>Gets or sets the recommended value.</summary>
        public double? RecommendedValue { get; set; }

        /// <summary>Gets or sets the sample timestamp.</summary>
        public DateTimeOffset Timestamp { get; set; }
    }
}
