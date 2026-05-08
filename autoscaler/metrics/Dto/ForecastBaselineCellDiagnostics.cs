namespace poolautoscaler.metrics.Dto
{
    /// <summary>
    /// Numeric pipeline for one baseline grid cell (same keys as <see cref="ForecastBaselineContributor"/> contributors for that slot).
    /// </summary>
    public sealed class ForecastBaselineCellDiagnostics
    {
        /// <summary>Aggregate of capped-corrected per-day slot values (cross-day combine).</summary>
        public double CrossDayAggregate { get; set; }

        /// <summary>After temporal smoothing when enabled; same as cross-day aggregate when smoothing is off.</summary>
        public double AfterSmoothBeforeBoost { get; set; }

        /// <summary>Cross-day aggregate at slot index − 1 before smoothing (null if absent or NaN in day profile).</summary>
        public double? SmoothNeighbourLeft { get; set; }

        /// <summary>Cross-day aggregate at slot index + 1 before smoothing (null if absent or NaN).</summary>
        public double? SmoothNeighbourRight { get; set; }

        /// <summary>Maps to <see cref="MetricForecastResult.ValueByDayAndHour"/> post-boost.</summary>
        public double FinalAfterBoost { get; set; }
    }
}
