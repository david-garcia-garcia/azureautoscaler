namespace poolautoscaler.configuration
{
    /// <summary>Per-resource configuration (enable, frequency, scaling configs).</summary>
    public class Resource
    {
        /// <summary>When false, resource is skipped.</summary>
        public bool? Enabled { get; set; }

        /// <summary>Resource instances keyed by resource ID.</summary>
        public Dictionary<string, ResourceInstance> Resources { get; set; }

        /// <summary>Polling interval (e.g. "4m").</summary>
        public string Frequency { get; set; }

        /// <summary>When true, only log what would change.</summary>
        public bool? WhatIf { get; set; }

        /// <summary>Parsed polling interval.</summary>
        public TimeSpan FrequencyParsed { get; set; }

        /// <summary>Scaling configurations keyed by ID.</summary>
        public Dictionary<string, ScalingConfiguration> ScalingConfigurations { get; set; }
    }
}
