using System.Text.Json.Serialization;

namespace poolautoscaler.resources.AzureDevops.Dto
{
    /// <summary>Response containing a session token from the API.</summary>
    public class SessionTokenResponse
    {
        /// <summary>Gets or sets the session token.</summary>
        [JsonPropertyName("token")]
        public string? Token { get; set; }
    }
}
