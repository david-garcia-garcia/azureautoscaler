namespace poolautoscaler.configuration
{
    /// <summary>Single resource instance (ID, resource ID, optional settings).</summary>
    public class ResourceInstance
    {
        /// <summary>Instance identifier.</summary>
        public string Id { get; set; }

        /// <summary>Azure resource ID.</summary>
        public string ResourceId { get; set; }

        /// <summary>
        /// Extra settings for resource types that require additional configuration.
        /// For Azure DevOps resources:
        ///   - Pat: Personal Access Token (or environment variable name if prefixed with "env:").
        ///   - PoolId: Agent pool ID to monitor for queue metrics.
        /// </summary>
        public Dictionary<string, string> Settings { get; set; }
    }
}
