using System.Text.Json;
using System.Text.Json.Serialization;

namespace poolautoscaler.resourcemanagement.Dto
{
    /// <summary>ARM resource snapshot from change history.</summary>
    public class ResourceHistoryItem
    {
        /// <summary>Resource ID.</summary>
        [JsonPropertyName("id")]
        public string Id { get; set; }

        /// <summary>Resource name.</summary>
        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>Resource type.</summary>
        [JsonPropertyName("type")]
        public string Type { get; set; }

        /// <summary>Tenant ID.</summary>
        [JsonPropertyName("tenantId")]
        public string TenantId { get; set; }

        /// <summary>Resource kind.</summary>
        [JsonPropertyName("kind")]
        public string Kind { get; set; }

        /// <summary>Azure region.</summary>
        [JsonPropertyName("location")]
        public string Location { get; set; }

        /// <summary>Resource group name.</summary>
        [JsonPropertyName("resourceGroup")]
        public string ResourceGroup { get; set; }

        /// <summary>Subscription ID.</summary>
        [JsonPropertyName("subscriptionId")]
        public string SubscriptionId { get; set; }

        /// <summary>Managed by.</summary>
        [JsonPropertyName("managedBy")]
        public string ManagedBy { get; set; }

        /// <summary>SKU.</summary>
        [JsonPropertyName("sku")]
        public ResourceSku Sku { get; set; }

        /// <summary>Plan.</summary>
        [JsonPropertyName("plan")]
        public object Plan { get; set; }

        /// <summary>Raw properties JSON.</summary>
        [JsonPropertyName("properties")]
        public JsonElement Properties { get; set; }

        /// <summary>Tags.</summary>
        [JsonPropertyName("tags")]
        public Dictionary<string, string> Tags { get; set; }

        /// <summary>Identity.</summary>
        [JsonPropertyName("identity")]
        public object Identity { get; set; }

        /// <summary>Zones.</summary>
        [JsonPropertyName("zones")]
        public object Zones { get; set; }

        /// <summary>Extended location.</summary>
        [JsonPropertyName("extendedLocation")]
        public object ExtendedLocation { get; set; }

        /// <summary>Snapshot timestamp.</summary>
        [JsonPropertyName("timestamp")]
        public DateTimeOffset Timestamp { get; set; }

        /// <summary>Deleted flag.</summary>
        [JsonPropertyName("deleted")]
        public int Deleted { get; set; }

        /// <summary>Row ID in history.</summary>
        [JsonPropertyName("rowId")]
        public string RowId { get; set; }

        /// <summary>Partial snapshot flag.</summary>
        [JsonPropertyName("partial")]
        public int Partial { get; set; }
    }
}
