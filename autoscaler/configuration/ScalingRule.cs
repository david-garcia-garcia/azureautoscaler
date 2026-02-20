using Microsoft.Extensions.Logging;
using poolautoscaler.strategies.Dto;

namespace poolautoscaler.configuration
{
    /// <summary>Single scaling rule (dimension, conditions, strategy).</summary>
    public class ScalingRule
    {
        /// <summary>Rule ID.</summary>
        public string Id { get; set; }

        /// <summary>
        /// The dimension of the resource that the scaler will act upon, depends on the type
        /// of the resource it could be the sku, the capacity, etc.
        /// </summary>
        public string Dimension { get; set; }

        /// <summary>Maximum allowed dimension value.</summary>
        public string DimensionValueMax { get; set; }

        /// <summary>Minimum allowed dimension value.</summary>
        public string DimensionValueMin { get; set; }

        /// <summary>
        /// Step size for ceiling rounding. When set, the dimension value will be rounded up to the nearest multiple of this step.
        /// </summary>
        public string DimensionValueCeilingStep { get; set; }

        /// <summary>
        /// Possible values: Fixed or Autoadjust.
        /// </summary>
        public string ScalingStrategy { get; set; }

        /// <summary>
        /// Threshold for the metric do upscale.
        /// </summary>
        public string ScaleUpCondition { get; set; }

        /// <summary>
        /// Threshold for the metric to downscale.
        /// </summary>
        public string ScaleDownCondition { get; set; }

        /// <summary>Expression for target dimension value.</summary>
        public string ScaleTarget { get; set; }

        /// <summary>Expression for scale-up target.</summary>
        public string ScaleUpTarget { get; set; }

        /// <summary>
        /// Threshold for the metric to downscale.
        /// </summary>
        public string ScaleDownTarget { get; set; }

        /// <summary>Compiled scale-up condition.</summary>
        public Func<MetricEvalDto, bool> ScaleUpConditionExpression { get; set; }

        /// <summary>Compiled scale-down condition.</summary>
        public Func<MetricEvalDto, bool> ScaleDownConditionExpression { get; set; }

        /// <summary>Compiled target expression.</summary>
        public Func<MetricEvalDto, string> ScaleTargetExpression { get; set; }

        /// <summary>Compiled scale-up target expression.</summary>
        public Func<MetricEvalDto, string> ScaleUpTargetExpression { get; set; }

        /// <summary>Compiled scale-down target expression.</summary>
        public Func<MetricEvalDto, string> ScaleDownTargetExpression { get; set; }

        /// <summary>Evaluates scale target from metric DTO.</summary>
        /// <param name="dto">The metric evaluation DTO.</param>
        /// <param name="logger">The logger.</param>
        /// <returns>Evaluated scale target value.</returns>
        public string ScaleTargetMethod(MetricEvalDto dto, ILogger logger)
        {
            logger.LogTrace($"ScaleTarget evaluation {this.ScaleTarget}");
            this.LogRuleDataSample(dto, this.ScaleTarget, logger);

            try
            {
                return this.ScaleTargetExpression.Invoke(dto);
            }
            catch (Exception ex)
            {
                // There is little to obtain from the inner exception when the error in the lambda
                throw new Exception($"Error evaluating scaling rule {this.Id}: " + ex.Message);
            }
        }

        /// <summary>Evaluates scale-up target from metric DTO.</summary>
        /// <param name="dto">The metric evaluation DTO.</param>
        /// <param name="logger">The logger.</param>
        /// <returns>Evaluated scale-up target value.</returns>
        public string ScaleUpTargetMethod(MetricEvalDto dto, ILogger logger)
        {
            logger.LogTrace($"ScaleUpTarget evaluation {this.ScaleUpTarget}");
            this.LogRuleDataSample(dto, this.ScaleDownTarget, logger);

            try
            {
                return this.ScaleUpTargetExpression.Invoke(dto);
            }
            catch (Exception ex)
            {
                // There is little to obtain from the inner exception when the error in the lambda
                throw new Exception($"Error evaluating scaling rule {this.Id}: " + ex.Message);
            }
        }

        /// <summary>Evaluates scale-down target from metric DTO.</summary>
        /// <param name="dto">The metric evaluation DTO.</param>
        /// <param name="logger">The logger.</param>
        /// <returns>Evaluated scale-down target value.</returns>
        public string ScaleDownTargetMethod(MetricEvalDto dto, ILogger logger)
        {
            logger.LogTrace($"ScaleDownTarget evaluation {this.ScaleDownTarget}");
            this.LogRuleDataSample(dto, this.ScaleDownTarget, logger);

            try
            {
                return this.ScaleDownTargetExpression.Invoke(dto);
            }
            catch (Exception ex)
            {
                // There is little to obtain from the inner exception when the error in the lambda
                throw new Exception($"Error evaluating scaling rule {this.Id}: " + ex.Message);
            }
        }

        /// <summary>Evaluates scale-up condition.</summary>
        /// <param name="dto">The metric evaluation DTO.</param>
        /// <param name="logger">The logger.</param>
        /// <returns>True if scale-up condition is met.</returns>
        public bool ScaleUpConditionMethod(MetricEvalDto dto, ILogger logger)
        {
            logger.LogTrace($"ScaleUpCondition evaluation {this.ScaleUpCondition}");
            this.LogRuleDataSample(dto, this.ScaleUpCondition, logger);

            try
            {
                return this.ScaleUpConditionExpression.Invoke(dto);
            }
            catch (Exception ex)
            {
                // There is little to obtain from the inner exception when the error in the lambda
                throw new Exception($"Error evaluating scaling rule {this.Id}: " + ex.Message);
            }
        }

        /// <summary>Evaluates scale-down condition.</summary>
        /// <param name="dto">The metric evaluation DTO.</param>
        /// <param name="logger">The logger.</param>
        /// <returns>True if scale-down condition is met.</returns>
        public bool ScaleDownConditionMethod(MetricEvalDto dto, ILogger logger)
        {
            logger.LogTrace($"ScaleDownCondition evaluation {this.ScaleDownCondition}");
            this.LogRuleDataSample(dto, this.ScaleDownCondition, logger);

            try
            {
                return this.ScaleDownConditionExpression.Invoke(dto);
            }
            catch (Exception ex)
            {
                // There is little to obtain from the inner exception when the error in the lambda
                throw new Exception($"Error evaluating scaling rule {this.Id}: " + ex.Message);
            }
        }

        /// <summary>Logs a sample of metric data used in the expression.</summary>
        /// <param name="dto">The metric evaluation DTO.</param>
        /// <param name="expression">The expression string.</param>
        /// <param name="logger">The logger.</param>
        public void LogRuleDataSample(MetricEvalDto dto, string expression, ILogger logger)
        {
            foreach (var m in dto.Metrics)
            {
                if (!expression.Contains(m.Key))
                {
                    continue;
                }

                logger.LogTrace($"Rule data sample for {m.Key} with points {m.Value.Values.Count}: {System.Text.Json.JsonSerializer.Serialize(m.Value.Values.FirstOrDefault())}");
            }
        }

        /// <summary>
        /// Do not scale up again until this amount of time has passed.
        /// </summary>
        public int ScaleUpCooldownSeconds { get; set; }

        /// <summary>
        /// Do not scale down again until this amount of time has passed.
        /// </summary>
        public int ScaleDownCooldownSeconds { get; set; }

        /// <summary>
        /// Optional metric ID to use for determining the last scale time for this specific rule.
        /// When set, the metric's time series is inspected to infer when this rule last effectively
        /// scaled (for example, when node_count changed).
        /// </summary>
        public string? LastScaleMetric { get; set; }

        /// <summary>
        /// Last known scale time used for this specific rule.
        /// This is updated at evaluation time and acts as a rule-level memory that survives
        /// gaps in ARM change history or metric windows. When determining the effective last
        /// scale for cooldowns, the newest of: metric-based timestamp, this value, and the
        /// resource-level LastScale is used.
        /// </summary>
        public DateTime? LastScale { get; set; }
    }
}
