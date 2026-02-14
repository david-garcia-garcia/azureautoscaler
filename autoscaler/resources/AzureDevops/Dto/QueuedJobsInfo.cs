namespace poolautoscaler.resources.AzureDevops.Dto
{
    /// <summary>
    /// Queued and running jobs info.
    /// </summary>
    public class QueuedJobsInfo
    {
        /// <summary>
        /// Gets or sets the queued jobs count.
        /// </summary>
        public int QueuedJobs { get; set; }

        /// <summary>
        /// Gets or sets the running jobs count.
        /// </summary>
        public int RunningJobs { get; set; }
    }
}
