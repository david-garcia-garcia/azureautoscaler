namespace poolautoscaler.resourcemanagement
{
    /// <summary>
    /// Exception thrown when a resource no longer exists in Azure (e.g., deleted).
    /// </summary>
    public class ResourceNotFoundException : Exception
    {
        /// <summary>Initializes a new instance of the <see cref="ResourceNotFoundException"/> class with resource ID and message.</summary>
        /// <param name="resourceId">The resource ID that was not found.</param>
        /// <param name="message">The exception message.</param>
        public ResourceNotFoundException(string resourceId, string message) : base(message)
        {
            this.ResourceId = resourceId;
        }

        /// <summary>Resource ID that was not found.</summary>
        public string ResourceId { get; }
    }
}
