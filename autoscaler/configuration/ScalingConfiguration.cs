namespace poolautoscaler.configuration
{
    /// <summary>Time-bounded scaling config (metrics, time window, rules).</summary>
    public class ScalingConfiguration
    {
        /// <summary>Configuration ID.</summary>
        public string Id { get; set; }

        /// <summary>
        /// The name of the metric this rule will evaluate.
        /// </summary>
        public Dictionary<string, Metric> Metrics { get; set; }

        /// <summary>
        /// When should this scaling configuration be applied.
        /// </summary>
        public TimeWindow TimeWindow { get; set; }

        /// <summary>
        /// When set to N, scale-down is blocked during UTC minutes 0 through N−1 of each clock hour (first N minutes of the hour).
        /// Scale-down is allowed from minute N through 59.
        /// </summary>
        public int? ScaleDownLockWindowMinutes { get; set; }

        /// <summary>
        /// When set to N, scale-up is blocked during UTC minutes N through 59 of each clock hour.
        /// Scale-up is allowed during minutes 0 through N−1.
        /// </summary>
        public int? ScaleUpAllowWindowMinutes { get; set; }

        /// <summary>
        /// The scaling rules.
        /// </summary>
        public Dictionary<string, ScalingRule> ScalingRules { get; set; }
    }
}
