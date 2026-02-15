using System.Text.Json.Serialization;

namespace poolautoscaler.resources.AzureDevops.Dto
{
    /// <summary>Azure DevOps meter resource from the API.</summary>
    public class MeterResource
    {
        /// <summary>Gets or sets the meter identifier.</summary>
        [JsonPropertyName("meterId")]
        public string? MeterId { get; set; }

        /// <summary>Gets or sets the purchase quantity (decimal from API, e.g. 1.0).</summary>
        [JsonPropertyName("purchaseQuantity")]
        public double? PurchaseQuantity { get; set; }

        /// <summary>Gets or sets the included quantity.</summary>
        [JsonPropertyName("includedQuantity")]
        public double? IncludedQuantity { get; set; }
    }
}
