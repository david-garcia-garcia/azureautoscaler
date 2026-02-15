using System.Text.Json.Serialization;

namespace poolautoscaler.resources.AzureDevops.Dto
{
    /// <summary>Response wrapper for meter resources from the API.</summary>
    public class MeterResourceResponse
    {
        /// <summary>Gets or sets the list of meter resources.</summary>
        [JsonPropertyName("value")]
        public List<MeterResource>? Value { get; set; }
    }
}
