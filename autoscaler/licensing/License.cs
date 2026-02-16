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

        /// <summary>
        /// Optional list of Azure subscription IDs allowed by this license. When null or empty, all subscriptions are allowed.
        /// When populated, only resources in these subscriptions may be processed.
        /// </summary>
        [JsonPropertyName("allowedSubscriptionIds")]
        public List<string>? AllowedSubscriptionIds { get; set; }

        /// <summary>
        /// Returns true if the given subscription ID is allowed by this license.
        /// When AllowedSubscriptionIds is null or empty, all subscriptions are allowed.
        /// When populated, only listed subscriptions are allowed. Resources without a subscription (e.g. Azure DevOps) are not allowed when the list is populated.
        /// </summary>
        /// <param name="subscriptionId">The subscription ID (null for non-ARM resources like Azure DevOps).</param>
        /// <returns>True if the resource may be processed.</returns>
        public bool IsSubscriptionAllowed(string? subscriptionId)
        {
            if (this.AllowedSubscriptionIds == null || this.AllowedSubscriptionIds.Count == 0)
            {
                return true;
            }

            return !string.IsNullOrEmpty(subscriptionId) && this.AllowedSubscriptionIds.Contains(subscriptionId);
        }
    }
}
