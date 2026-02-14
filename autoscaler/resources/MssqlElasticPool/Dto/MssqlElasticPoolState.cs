using Azure.ResourceManager.Sql.Models;

namespace poolautoscaler.resources.MssqlElasticPool.Dto
{
    /// <summary>
    /// Represents the SQL Elastic Pool scaling state.
    /// </summary>
    public class MssqlElasticPoolState
    {
        /// <summary>
        /// Gets or sets the SKU.
        /// </summary>
        public SqlSku? Sku { get; set; }

        /// <summary>
        /// Gets or sets the maximum size in bytes.
        /// </summary>
        public long? MaxSizeBytes { get; set; }

        /// <summary>
        /// Gets or sets the current used storage.
        /// </summary>
        public long? CurrentUsedStorage { get; set; }
    }
}
