using Azure.ResourceManager.MySql.FlexibleServers.Models;

namespace poolautoscaler.resources.MySqlFlexibleServer.Dto
{
    /// <summary>State DTO for a MySql Flexible Server (SKU, cores, IOPS).</summary>
    public class MySqlFlexibleServerState
    {
        /// <summary>Gets or sets the SKU.</summary>
        public MySqlFlexibleServerSku? Sku { get; set; }

        /// <summary>Gets or sets the core count.</summary>
        public int? CoreCount { get; set; }

        /// <summary>Gets or sets the IOPS.</summary>
        public int? Iops { get; set; }
    }
}
