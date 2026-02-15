namespace poolautoscaler.resourcemanagement.Dto
{
    /// <summary>Describes a patch to apply to a resource (data, disruptive flag, has-changes).</summary>
    public class ResourcePatchOperation
    {
        /// <summary>Gets or sets the patch data object.</summary>
        public Object PatchData { get; set; }

        /// <summary>Gets or sets a value indicating whether patching the resource is disruptive (e.g. downtime).</summary>
        public bool Disruptive { get; set; }

        /// <summary>Gets or sets a value indicating whether there are changes to apply to the actual resource.</summary>
        public bool HasChanges { get; set; }
    }
}
