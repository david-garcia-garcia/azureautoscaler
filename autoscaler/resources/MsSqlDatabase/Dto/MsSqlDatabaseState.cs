using Azure.ResourceManager.Sql.Models;

namespace poolautoscaler.resources.MsSqlDatabase.Dto
{
    /// <summary>
    /// Represents the SQL Database scaling state.
    /// </summary>
    public class MsSqlDatabaseState
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
        public double? CurrentUsedStorage { get; set; }
    }
}
