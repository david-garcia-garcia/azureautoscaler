using System.Text.Json.Serialization;

namespace poolautoscaler.resources.AzureDevops.Dto
{
    public class MeterResource
    {
        [JsonPropertyName("meterId")]
        public string? MeterId { get; set; }

        // These come as decimal values from the API (e.g., 1.0), so we use double
        [JsonPropertyName("purchaseQuantity")]
        public double? PurchaseQuantity { get; set; }

        [JsonPropertyName("includedQuantity")]
        public double? IncludedQuantity { get; set; }
    }
}
