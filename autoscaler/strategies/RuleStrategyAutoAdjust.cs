using Azure.Core;
using Microsoft.Extensions.Logging;
using poolautoscaler.configuration;
using poolautoscaler.dimensions;
using poolautoscaler.metrics.Dto;
using poolautoscaler.resourcemanagement;
using poolautoscaler.strategies.Dto;

namespace poolautoscaler.strategies
{
    /// <summary>Adjusts the dimension value based on the metric evaluation.</summary>
    internal class RuleStrategyAutoAdjust : IRuleStrategy
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

            string targetDimensionValue;

            evaluationMetrics._NextDimensionValue = (long step) => dimension.GetNextDimensionValue(resource, currentDimensionValue);
            evaluationMetrics._PreviousDimensionValue = (long step) => dimension.GetPreviousDimensionValue(resource, currentDimensionValue);

            if (scaleUp)
            {
                targetDimensionValue = rule.ScaleUpTargetMethod(evaluationMetrics, logger);
            }
            else if (scaleDown)
            {
                targetDimensionValue = rule.ScaleDownTargetMethod(evaluationMetrics, logger);
            }
            else
            {
                targetDimensionValue = currentDimensionValue;
            }

            logger.LogDebug(
                "Rule '{0}' evaluated: ScaleUp='{1}', ScaleDown='{2}', TargetDimensionValue='{3}', CurrentDimensionValue='{4}', NextDimensionValueStep1='{5}', PreviousDimensionValueStep1='{6}'",
                rule.Id,
                scaleUp ? "true" : "false",
                scaleDown ? "true" : "false",
                targetDimensionValue,
                currentDimensionValue,
                evaluationMetrics._NextDimensionValue(1),
                evaluationMetrics._PreviousDimensionValue(1));

            if ((!scaleDown) && (!scaleUp))
            {
                return targetDimensionValue;
            }

            bool belowMinimum = dimension.SmallerThan(targetDimensionValue, rule.DimensionValueMin, armResource);
            bool aboveMaximum = dimension.GreaterThan(targetDimensionValue, rule.DimensionValueMax, armResource);

            bool outOfRange = belowMinimum || aboveMaximum;

            // Caso límite donde se ha actuado sobre el recurso desde fuera del autoescaler, y a pesar de ser un scale down,
            // seguimos por encima del máximo
            bool isScaleDownButGreaterThanMax = dimension.SmallerThan(targetDimensionValue, currentDimensionValue, armResource)
                && dimension.GreaterThan(targetDimensionValue, rule.DimensionValueMax, armResource);

            if (isScaleDownButGreaterThanMax)
            {
                logger.LogWarning("Resource will scale down, but still above max configured range.");
            }

            if (outOfRange && !isScaleDownButGreaterThanMax)
            {
                if (currentDimensionValue != targetDimensionValue)
                {
                    if (belowMinimum)
                    {
                        logger.LogDebug(
                            "Cannot scale below minimum configured value. Current {0}. Target {1}. Min {2}. Adjusting to minimum.",
                            currentDimensionValue,
                            targetDimensionValue,
                            rule.DimensionValueMin);
                        return rule.DimensionValueMin;
                    }
                    else if (aboveMaximum)
                    {
                        logger.LogDebug(
                            "Cannot scale above maximum configured value. Current {0}. Target {1}. Max {2}. Adjusting to maximum.",
                            currentDimensionValue,
                            targetDimensionValue,
                            rule.DimensionValueMax);

                        return rule.DimensionValueMax;
                    }
                }

                return currentDimensionValue;
            }

            return targetDimensionValue;
        }

    }
}
