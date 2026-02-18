using Azure.Core;
using Microsoft.Extensions.Logging;
using poolautoscaler.configuration;
using poolautoscaler.dimensions;
using poolautoscaler.metrics.Dto;
using poolautoscaler.resourcemanagement;
using poolautoscaler.strategies.Dto;

namespace poolautoscaler.strategies
{
    /// <summary>Allows metric evaluation and an expression for a target metric value.</summary>
    internal class RuleStrategyFixed : IRuleStrategy
    {
        /// <inheritdoc/>
        public async Task<string> EvaluateTargetDimensionValue(
            ScalingRule rule,
            IDimension dimension,
            ResourceState resource,
            ILogger logger,
            TokenCredential credential,
            CancellationToken stoppingToken,
            Dictionary<string, MetricEvalDtoResult> metrics)
        {
            var evaluationMetrics = new MetricEvalDto();
            evaluationMetrics.Metrics = metrics;
            var rawResult = rule.ScaleTargetMethod(evaluationMetrics, logger);

            var result = rawResult;

            // Apply ceiling rounding if DimensionValueCeilingStep is specified
            if (!string.IsNullOrEmpty(rule.DimensionValueCeilingStep))
            {
                if (double.TryParse(result, out var resultValue) && double.TryParse(rule.DimensionValueCeilingStep, out var stepValue))
                {
                    if (stepValue > 0)
                    {
                        var remainder = resultValue % stepValue;
                        var ceilingValue = remainder == 0 ? resultValue : resultValue + stepValue - remainder;
                        result = ceilingValue.ToString();
                    }
                }
            }

            if (!string.IsNullOrEmpty(rule.DimensionValueMax))
            {
                if (dimension.Compare(resource.Resource, result, rule.DimensionValueMax) > 0)
                {
                    result = rule.DimensionValueMax;
                }
            }

            if (!string.IsNullOrEmpty(rule.DimensionValueMin))
            {
                if (dimension.Compare(resource.Resource, result, rule.DimensionValueMin) < 0)
                {
                    result = rule.DimensionValueMin;
                }
            }

            logger.LogDebug("Rule '{ruleId}' evaluated with: rawResult='{rawResult}', result='{result}'", rule.Id, rawResult, result);

            return result;
        }
    }
}
