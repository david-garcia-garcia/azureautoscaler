using System.Text.Json.Serialization;

namespace poolautoscaler.resourcemanagement.Dto
{
    /// <summary>SKU name and tier from ARM.</summary>
    public class ResourceSku
    {
        /// <summary>SKU name.</summary>
        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>SKU tier.</summary>
        [JsonPropertyName("tier")]
        public string Tier { get; set; }
    }
}
