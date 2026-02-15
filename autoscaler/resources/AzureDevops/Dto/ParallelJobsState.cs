namespace poolautoscaler.resources.AzureDevops.Dto
{
    /// <summary>
    /// Parallel jobs count state (hosted and private).
    /// </summary>
    public class ParallelJobsState
    {
        /// <summary>
        /// Gets or sets the hosted parallel jobs count.
        /// </summary>
        public int? HostedParallelJobs { get; set; }

        /// <summary>
        /// Gets or sets the private parallel jobs count.
        /// </summary>
        public int? PrivateParallelJobs { get; set; }
    }
}
