namespace poolautoscaler.resources
{
    /// <summary>
    /// Exception thrown when a resource no longer exists in Azure (e.g., deleted)
    /// </summary>
    public class ResourceNotFoundException : Exception
    {
        public ResourceNotFoundException(string resourceId, string message) : base(message)
        {
            this.ResourceId = resourceId;
        }

        public string ResourceId { get; }
    }
}

