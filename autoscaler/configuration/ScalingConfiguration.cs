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
        /// Size of window in a natural billable hours where scale downs are locked.
        /// </summary>
        public int? ScaleDownLockWindowMinutes { get; set; }

        /// <summary>
        /// Size of window in a natural billable hours where scale ups are allowed.
        /// </summary>
        public int? ScaleUpAllowWindowMinutes { get; set; }

        /// <summary>
        /// The scaling rules.
        /// </summary>
        public Dictionary<string, ScalingRule> ScalingRules { get; set; }
    }
}
