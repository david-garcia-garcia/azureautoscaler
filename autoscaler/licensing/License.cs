using System.Text.Json.Serialization;

namespace poolautoscaler.licensing
{
    /// <summary>
    /// Represents a license for the autoscaler application.
    /// </summary>
    public class License
    {
        /// <summary>Licensee name.</summary>
        [JsonPropertyName("licensedTo")]
        public string LicensedTo { get; set; } = string.Empty;

        /// <summary>Expiration date (UTC).</summary>
        [JsonPropertyName("expirationDate")]
        public DateTime ExpirationDate { get; set; }

        /// <summary>Maximum number of resources allowed.</summary>
        [JsonPropertyName("maxResources")]
        public int MaxResources { get; set; }
    }
}
