namespace poolautoscaler.metrics.Dto
{
    /// <summary>
    /// One historical daily-window contribution to a baseline cell before cross-day aggregation.
    /// </summary>
    public sealed class ForecastBaselineContributor
    {
        /// <summary>UTC calendar date of the source daily window.</summary>
        public DateTime SampleDateUtc { get; init; }

        /// <summary>Corrected slot value from that day (same normalization as baseline).</summary>
        public double Value { get; init; }

        /// <summary>Max raw observation in this slot before capped-correction multiplier is applied (diagnostics).</summary>
        public double RawSlotMaxUncorrected { get; init; }

        /// <summary>True when usage hit cap/throttle versus ForecastMetricMax (corrected upward).</summary>
        public bool Capped { get; init; }
    }
}
