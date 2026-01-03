using System.Text.Json.Serialization;

namespace poolautoscaler.licensing
{
    /// <summary>
    /// Represents a license for the autoscaler application
    /// </summary>
    public class License
    {
        [JsonPropertyName("licensedTo")]
        public string LicensedTo { get; set; } = string.Empty;

        [JsonPropertyName("expirationDate")]
        public DateTime ExpirationDate { get; set; }

        [JsonPropertyName("maxResources")]
        public int MaxResources { get; set; }
    }
}
