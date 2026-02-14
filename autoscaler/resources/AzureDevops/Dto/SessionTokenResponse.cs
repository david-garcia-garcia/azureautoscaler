using System.Text.Json.Serialization;

namespace poolautoscaler.resources.AzureDevops.Dto
{
    public class SessionTokenResponse
    {
        [JsonPropertyName("token")]
        public string? Token { get; set; }
    }
}
