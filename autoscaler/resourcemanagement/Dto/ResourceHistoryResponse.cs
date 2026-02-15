using System.Text.Json.Serialization;

namespace poolautoscaler.resourcemanagement.Dto
{
    /// <summary>
    /// Response containing ARM change history snapshots.
    /// </summary>
    public class ResourceHistoryResponse
    {
        /// <summary>
        /// Gets or sets the number of snapshots.
        /// </summary>
        [JsonPropertyName("count")]
        public int Count { get; set; }

        /// <summary>
        /// Gets or sets the list of resource snapshots.
        /// </summary>
        [JsonPropertyName("snapshots")]
        public List<ResourceHistoryItem> Snapshots { get; set; }
    }
}
