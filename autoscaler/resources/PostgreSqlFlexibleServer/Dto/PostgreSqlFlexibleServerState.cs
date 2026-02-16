using Azure.ResourceManager.PostgreSql.FlexibleServers.Models;

namespace poolautoscaler.resources.PostgreSqlFlexibleServer.Dto
{
    /// <summary>State DTO for a PostgreSQL Flexible Server (SKU, cores, IOPS).</summary>
    public class PostgreSqlFlexibleServerState
    {
        /// <summary>Gets or sets the SKU.</summary>
        public PostgreSqlFlexibleServerSku? Sku { get; set; }

        /// <summary>Gets or sets the core count.</summary>
        public int? CoreCount { get; set; }

        /// <summary>Gets or sets the IOPS.</summary>
        public int? Iops { get; set; }
    }
}
