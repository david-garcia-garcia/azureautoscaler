using System.Text.Json.Serialization;

namespace poolautoscaler.resources.AzureDevops.Dto
{
    /// <summary>
    /// Azure DevOps agent pools API response.
    /// </summary>
    public class AgentPoolsResponse
    {
        /// <summary>
        /// Gets or sets the list of agent pools.
        /// </summary>
        [JsonPropertyName("value")]
        public List<AgentPool>? Value { get; set; }
    }
}
