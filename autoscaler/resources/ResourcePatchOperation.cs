using Azure.ResourceManager.Models;

namespace poolautoscaler.resources
{
    public class ResourcePatchOperation
    {
        /// <summary>
        /// The patch object
        /// </summary>
        public Object PatchData { get; set; }

        /// <summary>
        /// If there is associated downtime with patching the resource
        /// </summary>
        public bool Disruptive { get; set; }

        /// <summary>
        /// If there are any changes to apply to the actual resource
        /// </summary>
        public bool HasChanges { get; set; }
    }
}
