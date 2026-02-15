using System.Text.Json.Serialization;

namespace poolautoscaler.resourcemanagement.Dto
{
    /// <summary>SKU name and tier from ARM.</summary>
    public class ResourceSku
    {
        /// <summary>Gets or sets the SKU name.</summary>
        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>Gets or sets the SKU tier.</summary>
        [JsonPropertyName("tier")]
        public string Tier { get; set; }
    }
}
