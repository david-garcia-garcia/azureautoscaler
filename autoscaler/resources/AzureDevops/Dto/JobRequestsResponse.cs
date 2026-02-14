using System.Text.Json.Serialization;

namespace poolautoscaler.resources.AzureDevops.Dto
{
    /// <summary>
    /// Azure DevOps job requests API response.
    /// </summary>
    public class JobRequestsResponse
    {
        /// <summary>
        /// Gets or sets the list of job requests.
        /// </summary>
        [JsonPropertyName("value")]
        public List<JobRequest>? Value { get; set; }
    }
}
