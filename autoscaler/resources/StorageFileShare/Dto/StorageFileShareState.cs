namespace poolautoscaler.resources.StorageFileShare.Dto
{
    /// <summary>
    /// Represents the storage file share state.
    /// </summary>
    public class StorageFileShareState
    {
        /// <summary>
        /// Gets or sets the share quota in GB.
        /// </summary>
        public int? ShareQuotaGb { get; set; }

        /// <summary>
        /// Gets or sets the share usage in bytes.
        /// </summary>
        public long? ShareUsageBytes { get; set; }
    }
}
