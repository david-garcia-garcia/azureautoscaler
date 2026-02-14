using System.Text.Json.Serialization;

namespace poolautoscaler.resources.AzureDevops.Dto
{
    public class MeterResourceResponse
    {
        [JsonPropertyName("value")]
        public List<MeterResource>? Value { get; set; }
    }
}
