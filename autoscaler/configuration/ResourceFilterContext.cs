namespace poolautoscaler.configuration
{
    /// <summary>
    /// Context passed to a <see cref="ResourceInstance.ResourceFilter"/> lambda expression.
    /// Provides the resource name, Azure tags, and the raw ARM resource object.
    ///
    /// Resource-type-specific properties are accessed directly via <see cref="Resource"/>
    /// using the same Dynamic LINQ member-access pattern as <c>CustomMetricDataContext.Resource</c>.
    ///
    /// Example filter (standalone DTU SQL databases only):
    /// <code>
    /// (r) => r.Resource.Data.Sku.Family == null &amp;&amp; r.Resource.Data.Sku.Name != "ElasticPool"
    /// </code>
    /// </summary>
    public class ResourceFilterContext
    {
        /// <summary>The name of the resource (database name, pool name, share name, etc.).</summary>
        public string ResourceName { get; set; }

        /// <summary>Azure resource tags at expansion time. Empty dictionary if the resource has no tags.</summary>
        public IDictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// The raw ARM resource object (e.g. <c>SqlDatabaseResource</c>, <c>ContainerServiceAgentPoolResource</c>).
        /// Dynamic LINQ resolves member access against the actual runtime type.
        /// </summary>
        public object Resource { get; set; }
    }
}
