using Azure.Core;
using Microsoft.Extensions.Logging;
using poolautoscaler.resources;

namespace poolautoscaler.strategies
{
    /// <summary>
    /// Adjust the dimension value based on the metric evaluation
    /// </summary>
    internal class RuleStrategyAutoAdjust : IRuleStrategy
    {
        public async Task<string> EvaluateTargetDimensionValue(ScalingRule rule,
            IDimension dimension,
            ResourceState resource,
            ILogger logger,
            TokenCredential credential,
            CancellationToken stoppingToken,
            Dictionary<string, MetricEvalDtoResult> metrics)
        {
            var armResource = resource.Resource;

            var evaluationMetrics = new MetricEvalDto();
            evaluationMetrics.Metrics = metrics;

            string currentDimensionValue = dimension.GetCurrentDimensionValue(resource);

            bool scaleUp = rule.ScaleUpConditionMethod(evaluationMetrics, logger);
            bool scaleDown = rule.ScaleDownConditionMethod(evaluationMetrics, logger);

            if (scaleDown && scaleUp)
            {
                logger.LogWarning("Both scale up and scale down conditions were met. Scale up takes precedence.");
            }

            string nextDimensionValue = null;

            evaluationMetrics._NextDimensionValue = (long step) => dimension.GetNextDimensionValue(resource, currentDimensionValue);
            evaluationMetrics._PreviousDimensionValue = (long step) => dimension.GetPreviousDimensionValue(resource, currentDimensionValue);

            if (scaleUp)
            {
                nextDimensionValue = rule.ScaleUpTargetMethod(evaluationMetrics, logger);
            }
            else if (scaleDown)
            {
                nextDimensionValue = rule.ScaleDownTargetMethod(evaluationMetrics, logger);
            }
            else
            {
                return currentDimensionValue;
            }

            bool belowMinimum = dimension.SmallerThan(nextDimensionValue, rule.DimensionValueMin, armResource);
            bool aboveMaximum = dimension.GreaterThan(nextDimensionValue, rule.DimensionValueMax, armResource);

            bool outOfRange = belowMinimum || aboveMaximum;

            // Caso límite donde se ha actuado sobre el recurso desde fuera del autoescaler, y a pesar de ser un scale down,
            // seguimos por encima del máximo
            bool isScaleDownButGreaterThanMax = dimension.SmallerThan(nextDimensionValue, currentDimensionValue, armResource)
                && dimension.GreaterThan(nextDimensionValue, rule.DimensionValueMax, armResource);

            if (isScaleDownButGreaterThanMax)
            {
                logger.LogWarning("Resource will scale down, but still above max configured range.");
            }

            if (belowMinimum)
            {
                logger.LogDebug(
                    "Cannot scale below minimum configured value. Current {0}. Target {1}. Min {2}. Adjusting to minimum.",

                    currentDimensionValue, nextDimensionValue, rule.DimensionValueMin);
                return rule.DimensionValueMin;
            }

            if (outOfRange && !isScaleDownButGreaterThanMax)
            {
                if (aboveMaximum)
                {
                    logger.LogDebug(
                        "Cannot scale above maximum configured value. Current {0}. Target {1}. Max {2}. Adjusting to maximum.",
                        currentDimensionValue, nextDimensionValue, rule.DimensionValueMax);

                    return rule.DimensionValueMax;
                }

                return currentDimensionValue;
            }

            return nextDimensionValue;
        }

    }
}
