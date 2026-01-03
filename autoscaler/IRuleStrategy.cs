using Azure.Core;
using Microsoft.Extensions.Logging;
using poolautoscaler.resources;
using poolautoscaler.strategies;

namespace poolautoscaler
{
    internal interface IRuleStrategy
    {
        Task<string> EvaluateTargetDimensionValue(ScalingRule rule,
            IDimension dimension,
            ResourceState resource,
            ILogger logger,
            TokenCredential credential,
            CancellationToken stoppingToken,
            Dictionary<string, MetricEvalDtoResult> metrics);
    }
}
