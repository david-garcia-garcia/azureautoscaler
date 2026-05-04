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

        /// <summary>
        /// Optional C# lambda expression compiled to <c>Func&lt;ResourceFilterContext, bool&gt;</c> at startup.
        /// When set, only wildcard-expanded resources for which the expression returns <c>true</c> are included
        /// in the discovered resource set. Non-wildcard (literal) resource IDs are always included.
        ///
        /// Example — standalone DTU SQL databases only:
        /// <code>
        /// ResourceFilter: "(r) => r.Resource.Data.Sku.Family == null &amp;&amp; r.Resource.Data.Sku.Name != \"ElasticPool\""
        /// </code>
        /// Access resource-type-specific properties via <c>r.Resource</c> (Dynamic LINQ resolves members
        /// against the actual runtime type). Use <c>r.Tags</c> for tag-based filtering.
        /// </summary>
        public string ResourceFilter { get; set; }

        /// <summary>Compiled form of <see cref="ResourceFilter"/>. Populated by <see cref="Configuration.PrepareAndValidate"/>.</summary>
        public Func<ResourceFilterContext, bool> ResourceFilterExpression { get; set; }
    }
}
