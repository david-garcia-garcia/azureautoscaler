using Microsoft.Extensions.Logging;
using poolautoscaler.configuration;
using poolautoscaler.metrics.Dto;
using poolautoscaler.resourcemanagement;

namespace poolautoscaler.metrics
{
    /// <summary>
    /// Gathers metrics for scaling rule evaluation. Injectable for testing.
    /// </summary>
    internal interface IMetricsGatherer
    {
        /// <summary>
        /// Gathers metric data for the given scaling configuration and resource.
        /// </summary>
        /// <param name="setting">The scaling configuration containing metric definitions.</param>
        /// <param name="state">The resource state.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <param name="logger">The logger.</param>
        /// <returns>Dictionary of metric ID to evaluation result.</returns>
        Task<Dictionary<string, MetricEvalDtoResult>> GatherMetricsAsync(
            ScalingConfiguration setting,
            ResourceState state,
            CancellationToken cancellationToken,
            ILogger logger);
    }
}
