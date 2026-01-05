using Azure.Core;
using Azure.ResourceManager;
using Microsoft.Extensions.Logging;
using poolautoscaler.resources;

namespace poolautoscaler.strategies
{
    /// <summary>
    /// Forecast strategy that uses historical metrics to predict future capacity needs
    /// </summary>
    internal class RuleStrategyForecast : IRuleStrategy
    {
        public async Task<string> EvaluateTargetDimensionValue(ScalingRule rule,
            IDimension dimension,
            ResourceState resource,
            ILogger logger,
            TokenCredential credential,
            CancellationToken stoppingToken,
            Dictionary<string, MetricEvalDtoResult> metrics,
            ScalingConfiguration scalingConfiguration)
        {
            var armResource = resource.Resource;
            var resourceId = armResource.Id.ToString();

            if (scalingConfiguration == null)
            {
                logger.LogError($"Scaling configuration is null for rule '{rule.Id}'");
                return dimension.GetCurrentDimensionValue(resource);
            }

            var forecastEngine = new ForecastEngine(resourceId, logger);

            try
            {
                // Get ArmClient from credential - we'll need to create it
                // Actually, ForecastEngine doesn't need ArmClient, only credential
                var forecast = await forecastEngine.GenerateForecast(
                    null, // ArmClient not needed for forecast engine
                    credential,
                    stoppingToken,
                    scalingConfiguration,
                    rule);

                // Get the forecast for today
                var today = DateTime.UtcNow.DayOfWeek;
                var forecastedValue = forecast.Forecast.ContainsKey(today) ? forecast.Forecast[today] : 0;

                if (forecastedValue <= 0)
                {
                    logger.LogWarning($"No forecast available for {today}, keeping current dimension value");
                    return dimension.GetCurrentDimensionValue(resource);
                }

                // Round forecasted value to integer (most dimensions use integer values)
                var roundedValue = Math.Round(forecastedValue);
                
                // Apply ceiling step if specified (same as Fixed strategy)
                if (!string.IsNullOrEmpty(rule.DimensionValueCeilingStep))
                {
                    if (double.TryParse(rule.DimensionValueCeilingStep, out var stepValue) && stepValue > 0)
                    {
                        var remainder = roundedValue % stepValue;
                        roundedValue = remainder == 0 ? roundedValue : roundedValue + stepValue - remainder;
                    }
                }

                // Convert to string (dimension values are strings)
                var targetValue = roundedValue.ToString("0");

                // Apply min/max constraints using dimension comparison
                if (!string.IsNullOrEmpty(rule.DimensionValueMin))
                {
                    if (dimension.SmallerThan(targetValue, rule.DimensionValueMin, armResource))
                    {
                        logger.LogDebug(
                            "Forecasted value {0} is below minimum {1}. Using minimum.",
                            targetValue,
                            rule.DimensionValueMin);
                        targetValue = rule.DimensionValueMin;
                    }
                }

                if (!string.IsNullOrEmpty(rule.DimensionValueMax))
                {
                    if (dimension.GreaterThan(targetValue, rule.DimensionValueMax, armResource))
                    {
                        logger.LogDebug(
                            "Forecasted value {0} is above maximum {1}. Using maximum.",
                            targetValue,
                            rule.DimensionValueMax);
                        targetValue = rule.DimensionValueMax;
                    }
                }

                var currentValue = dimension.GetCurrentDimensionValue(resource);

                logger.LogDebug(
                    "Forecast strategy: Current value {0}, Forecasted value {1} (rounded to {2}), Target value {3}",
                    currentValue,
                    forecastedValue,
                    roundedValue,
                    targetValue);

                return targetValue;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error generating forecast for rule '{0}': {1}", rule.Id, ex.Message);
                // Fall back to current value on error
                return dimension.GetCurrentDimensionValue(resource);
            }
        }
    }
}
