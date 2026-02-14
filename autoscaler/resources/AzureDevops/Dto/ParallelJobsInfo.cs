namespace poolautoscaler.resources.AzureDevops.Dto
{
    /// <summary>
    /// Parallel jobs capacity info.
    /// </summary>
    public class ParallelJobsInfo
    {
        /// <summary>
        /// Gets or sets the hosted parallel jobs count.
        /// </summary>
        public int HostedParallelJobs { get; set; }

        /// <summary>
        /// Gets or sets the private parallel jobs count.
        /// </summary>
        public int PrivateParallelJobs { get; set; }
    }
}
