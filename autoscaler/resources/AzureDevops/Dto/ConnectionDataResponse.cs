using System.Text.Json.Serialization;

namespace poolautoscaler.resources.AzureDevops.Dto
{
    /// <summary>
    /// Azure DevOps connection data API response.
    /// </summary>
    public class ConnectionDataResponse
    {
        /// <summary>
        /// Gets or sets the instance ID.
        /// </summary>
        [JsonPropertyName("instanceId")]
        public string? InstanceId { get; set; }
    }
}
