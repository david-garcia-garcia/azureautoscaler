using Azure.ResourceManager.MySql.FlexibleServers.Models;

namespace poolautoscaler.resources.MySqlFlexibleServer.Dto
{
    public class MySqlFlexibleServerState
    {
        public MySqlFlexibleServerSku? Sku { get; set; }

        public int? CoreCount { get; set; }

        public int? Iops { get; set; }
    }
}
