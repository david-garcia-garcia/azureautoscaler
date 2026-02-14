using Azure.Core;
using Microsoft.Extensions.Logging;
using poolautoscaler.configuration;
using poolautoscaler.dimensions;
using poolautoscaler.resourcemanagement;

using poolautoscaler.metrics.Dto;

namespace poolautoscaler.strategies
{
    /// <summary>Strategy that evaluates the target dimension value from a rule and metrics.</summary>
    internal interface IRuleStrategy
    {
        /// <summary>Evaluates and returns the target dimension value.</summary>
        /// <param name="rule">The scaling rule.</param>
        /// <param name="dimension">The dimension to evaluate.</param>
        /// <param name="resource">The resource state.</param>
        /// <param name="logger">The logger.</param>
        /// <param name="credential">The token credential.</param>
        /// <param name="stoppingToken">Cancellation token.</param>
        /// <param name="metrics">Current metric results keyed by name.</param>
        /// <returns>The target dimension value.</returns>
        Task<string> EvaluateTargetDimensionValue(
            ScalingRule rule,
            IDimension dimension,
            ResourceState resource,
            ILogger logger,
            TokenCredential credential,
            CancellationToken stoppingToken,
            Dictionary<string, MetricEvalDtoResult> metrics);
    }
}
