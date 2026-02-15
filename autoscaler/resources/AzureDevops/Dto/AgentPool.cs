using System.Text.Json.Serialization;

namespace poolautoscaler.resources.AzureDevops.Dto
{
    /// <summary>
    /// Azure DevOps agent pool DTO.
    /// </summary>
    public class AgentPool
    {
        /// <summary>
        /// Gets or sets the pool ID.
        /// </summary>
        [JsonPropertyName("id")]
        public int Id { get; set; }

        /// <summary>
        /// Gets or sets the pool name.
        /// </summary>
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the pool is hosted.
        /// </summary>
        [JsonPropertyName("isHosted")]
        public bool IsHosted { get; set; }

        /// <summary>
        /// Gets or sets the pool type.
        /// </summary>
        [JsonPropertyName("poolType")]
        public string? PoolType { get; set; }
    }
}
