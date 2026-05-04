using poolautoscaler.configuration;

namespace poolautoscaler.resourcemanagement.Dto
{
    /// <summary>
    /// Result of wildcard resource expansion: a resolved resource ID paired with the filter context
    /// built from the ARM data already fetched during expansion.
    /// </summary>
    public class ExpandedResource
    {
        /// <summary>The fully resolved Azure resource ID.</summary>
        public string ResourceId { get; set; }

        /// <summary>
        /// Filter context populated from the ARM resource data. Null for non-wildcard (literal) resource IDs,
        /// in which case <see cref="ResourceInstance.ResourceFilter"/> is not evaluated and the resource is always included.
        /// </summary>
        public ResourceFilterContext? Context { get; set; }
    }
}
