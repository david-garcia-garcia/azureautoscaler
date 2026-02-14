using System.Text.Json.Serialization;

namespace poolautoscaler.resources.AzureDevops.Dto
{
    /// <summary>
    /// Azure DevOps job request DTO.
    /// </summary>
    public class JobRequest
    {
        /// <summary>
        /// Gets or sets the assign time.
        /// </summary>
        [JsonPropertyName("assignTime")]
        public DateTime? AssignTime { get; set; }

        /// <summary>
        /// Gets or sets the result.
        /// </summary>
        [JsonPropertyName("result")]
        public object? Result { get; set; }
    }
}
