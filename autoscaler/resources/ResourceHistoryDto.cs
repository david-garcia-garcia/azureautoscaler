using System.Text.Json;
using System.Text.Json.Serialization;

namespace poolautoscaler.resources
{
    public class ResourceHistoryResponse
    {
        [JsonPropertyName("count")]
        public int Count { get; set; }

        [JsonPropertyName("snapshots")]
        public List<ResourceHistoryItem> Snapshots { get; set; }
    }

    public class ResourceHistoryItem
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("tenantId")]
        public string TenantId { get; set; }

        [JsonPropertyName("kind")]
        public string Kind { get; set; }

        [JsonPropertyName("location")]
        public string Location { get; set; }

        [JsonPropertyName("resourceGroup")]
        public string ResourceGroup { get; set; }

        [JsonPropertyName("subscriptionId")]
        public string SubscriptionId { get; set; }

        [JsonPropertyName("managedBy")]
        public string ManagedBy { get; set; }

        [JsonPropertyName("sku")]
        public ResourceSku Sku { get; set; }

        [JsonPropertyName("plan")]
        public object Plan { get; set; }

        [JsonPropertyName("properties")]
        public JsonElement Properties { get; set; }

        [JsonPropertyName("tags")]
        public Dictionary<string, string> Tags { get; set; }

        [JsonPropertyName("identity")]
        public object Identity { get; set; }

        [JsonPropertyName("zones")]
        public object Zones { get; set; }

        [JsonPropertyName("extendedLocation")]
        public object ExtendedLocation { get; set; }

        [JsonPropertyName("timestamp")]
        public DateTimeOffset Timestamp { get; set; }

        [JsonPropertyName("deleted")]
        public int Deleted { get; set; }

        [JsonPropertyName("rowId")]
        public string RowId { get; set; }

        [JsonPropertyName("partial")]
        public int Partial { get; set; }
    }

    public class ResourceSku
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("tier")]
        public string Tier { get; set; }
    }
}
