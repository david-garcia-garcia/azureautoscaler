using Azure.Monitor.Query.Models;
using Microsoft.Extensions.Logging;
using poolautoscaler.strategies;
using poolautoscaler.utils;
using System.Data;

public class Configuration
{
    public string AzureCredentialType { get; set; }

    public bool DefaultResourceWhatIf { get; set; } = true;

    public bool DefaultResourceEnabled { get; set; } = true;

    public string DefaultResourceFrequency { get; set; } = "4m";

    /// <summary>
    /// How often to refresh and expand resource lists to discover new resources (default: 1 hour)
    /// </summary>
    public string ResourceDiscoveryFrequency { get; set; } = "1h";

    public TimeSpan ResourceDiscoveryFrequencyParsed { get; set; }

    /// <summary>
    /// 
    /// </summary>
    public List<Resource> Resources { get; set; }

    public void PrepareAndValidate(ILogger logger)
    {
        ResourceDiscoveryFrequencyParsed = DurationParser.ParseDuration(ResourceDiscoveryFrequency);

        if (Resources == null) return;

        foreach (var resource in Resources)
        {
            if (string.IsNullOrWhiteSpace(resource.Frequency))
            {
                resource.Frequency = this.DefaultResourceFrequency;
            }

            if (resource.WhatIf == null)
            {
                resource.WhatIf = this.DefaultResourceWhatIf;
            }

            if (resource.Enabled == null)
            {
                resource.Enabled = this.DefaultResourceEnabled;
            }

            resource.FrequencyParsed = DurationParser.ParseDuration(resource.Frequency);

            if (resource.ScalingConfigurations == null) continue;

            foreach (var scalingConfiguration in resource.ScalingConfigurations)
            {
                var config = scalingConfiguration.Value;
                config.Id = scalingConfiguration.Key;

                if (config.ScalingRules == null) continue;

                config.TimeWindow.TimeZoneParsed = TimeZoneInfo.FindSystemTimeZoneById(config.TimeWindow.TimeZone);

                config.TimeWindow.StartTimeParsed = TimeSpan.Parse(config.TimeWindow.StartTime);
                config.TimeWindow.EndTimeParsed = TimeSpan.Parse(config.TimeWindow.EndTime);


                if (config.Metrics != null)
                {
                    foreach (var metricConfig in config.Metrics)
                    {
                        var metric = metricConfig.Value;
                        metric.Id = metricConfig.Key;

                        if (metric.Aggregations != null)
                        {
                            metric.ParsedAggregations = metric.Aggregations
                                .Select((i) => (MetricAggregationType)Enum.Parse(typeof(MetricAggregationType), i))
                                .ToList();
                        }

                        if (string.IsNullOrWhiteSpace(metric.Name))
                        {
                            throw new Exception($"Metric {metric.Id} in {scalingConfiguration.Key} does not have a name.");
                        }

                        if (!string.IsNullOrEmpty(metric.Transform))
                        {
                            metric.TransformExpression =
                                (Func<MetricEvalDtoResultValue, MetricEvalDtoResultValue>)ExpressionParserUtils.ParseExpression(metric.Transform,
                                    "value", typeof(MetricEvalDtoResultValue), typeof(MetricEvalDtoResultValue), 1);
                        }
                        else
                        {
                            metric.TransformExpression = a => a;
                        }
                    }
                }

                foreach (var scalingRule in config.ScalingRules)
                {
                    var rule = scalingRule.Value;
                    rule.Id = scalingRule.Key;

                    if (!string.IsNullOrEmpty(rule.ScaleUpCondition))
                    {
                        rule.ScaleUpConditionExpression =
                            (Func<MetricEvalDto, bool>)ExpressionParserUtils.ParseExpression(rule.ScaleUpCondition,
                                "data", typeof(MetricEvalDto), typeof(bool), 1);
                    }

                    if (!string.IsNullOrEmpty(rule.ScaleDownCondition))
                    {
                        rule.ScaleDownConditionExpression =
                            (Func<MetricEvalDto, bool>)ExpressionParserUtils.ParseExpression(
                                rule.ScaleDownCondition, "data", typeof(MetricEvalDto), typeof(bool), 1);
                    }

                    if (!string.IsNullOrEmpty(rule.ScaleTarget))
                    {
                        rule.ScaleTargetExpression =
                            (Func<MetricEvalDto, string>)ExpressionParserUtils.ParseExpression(rule.ScaleTarget,
                                "data", typeof(MetricEvalDto), typeof(string), 1);
                    }

                    if (!string.IsNullOrEmpty(rule.ScaleUpTarget))
                    {
                        rule.ScaleUpTargetExpression =
                            (Func<MetricEvalDto, string>)ExpressionParserUtils.ParseExpression(rule.ScaleUpTarget,
                                "data", typeof(MetricEvalDto), typeof(string), 1);
                    }

                    if (!string.IsNullOrEmpty(rule.ScaleDownTarget))
                    {
                        rule.ScaleDownTargetExpression =
                            (Func<MetricEvalDto, string>)ExpressionParserUtils.ParseExpression(rule.ScaleDownTarget,
                                "data", typeof(MetricEvalDto), typeof(string), 1);
                    }
                }
            }
        }
    }
}

public class Resource
{
    /// <summary>
    /// 
    /// </summary>
    public bool? Enabled { get; set; }

    /// <summary>
    /// The Azure resource ID
    /// </summary>
    public Dictionary<string, ResourceInstance> Resources { get; set; }

    /// <summary>
    /// 
    /// </summary>
    public string Frequency { get; set; }

    /// <summary>
    /// 
    /// </summary>
    public bool? WhatIf { get; set; }

    public TimeSpan FrequencyParsed { get; set; }

    /// <summary>
    /// The scaling configurations
    /// </summary>
    public Dictionary<string, ScalingConfiguration> ScalingConfigurations { get; set; }
}

public class ResourceInstance
{
    public string Id { get; set; }

    public string ResourceId { get; set; }
}

public class ScalingConfiguration
{
    /// <summary>
    /// Id of the configuration
    /// </summary>
    public string Id { get; set; }

    /// <summary>
    /// The name of the metric this rule will evaluate
    /// </summary>
    public Dictionary<string, Metric> Metrics { get; set; }

    /// <summary>
    /// When should this scaling configuration be applied.
    /// </summary>
    public TimeWindow TimeWindow { get; set; }

    /// <summary>
    /// Size of window in a natural billable hours where scale downs are locked.
    /// </summary>
    public int? ScaleDownLockWindowMinutes { get; set; }

    /// <summary>
    /// Size of window in a natural billable hours where scale ups are allowed.
    /// </summary>
    public int? ScaleUpAllowWindowMinutes { get; set; }

    /// <summary>
    /// The scaling rules
    /// </summary>
    public Dictionary<string, ScalingRule> ScalingRules { get; set; }
}

public class TimeWindow
{
    public string TimeZone { get; set; } = "UTC";

    public TimeZoneInfo TimeZoneParsed { get; set; }

    public string Days { get; set; }

    public string Months { get; set; }

    public string StartTime { get; set; }

    public TimeSpan? StartTimeParsed { get; set; }

    public string EndTime { get; set; }

    public TimeSpan? EndTimeParsed { get; set; }
}

public class ScalingRule
{
    public string Id { get; set; }

    /// <summary>
    /// The dimension of the resource that the scaler will act upon, depends on the type
    /// of the resource it could be the sku, the capacity, et.c
    /// </summary>
    public string Dimension { get; set; }

    /// <summary>
    /// 
    /// </summary>
    public string DimensionValueMax { get; set; }

    /// <summary>
    /// 
    /// </summary>
    public string DimensionValueMin { get; set; }

    /// <summary>
    /// Step size for ceiling rounding. When set, the dimension value will be rounded up to the nearest multiple of this step.
    /// </summary>
    public string DimensionValueCeilingStep { get; set; }

    /// <summary>
    /// Possible values: Fixed or Autoadjust
    /// </summary>
    public string ScalingStrategy { get; set; }

    /// <summary>
    /// Threshold for the metric do upscale
    /// </summary>
    public string ScaleUpCondition { get; set; }

    /// <summary>
    /// Threshold for the metric to downscale
    /// </summary>
    public string ScaleDownCondition { get; set; }

    public string ScaleTarget { get; set; }

    public string ScaleUpTarget { get; set; }

    /// <summary>
    /// Threshold for the metric to downscale
    /// </summary>
    public string ScaleDownTarget { get; set; }

    public Func<MetricEvalDto, bool> ScaleUpConditionExpression { get; set; }
    public Func<MetricEvalDto, bool> ScaleDownConditionExpression { get; set; }
    public Func<MetricEvalDto, string> ScaleTargetExpression { get; set; }
    public Func<MetricEvalDto, string> ScaleUpTargetExpression { get; set; }
    public Func<MetricEvalDto, string> ScaleDownTargetExpression { get; set; }

    public string ScaleTargetMethod(MetricEvalDto dto, ILogger logger)
    {
        logger.LogTrace($"ScaleTarget evaluation {this.ScaleTarget}");
        this.LogRuleDataSample(dto, this.ScaleTarget, logger);
        string result;
        result = this.ScaleTargetExpression.Invoke(dto);
        return result;
    }

    public string ScaleUpTargetMethod(MetricEvalDto dto, ILogger logger)
    {
        logger.LogTrace($"ScaleUpTarget evaluation {this.ScaleUpTarget}");
        this.LogRuleDataSample(dto, this.ScaleDownTarget, logger);
        string result;
        result = this.ScaleUpTargetExpression.Invoke(dto);
        return result;
    }

    public string ScaleDownTargetMethod(MetricEvalDto dto, ILogger logger)
    {
        logger.LogTrace($"ScaleDownTarget evaluation {this.ScaleDownTarget}");
        this.LogRuleDataSample(dto, this.ScaleDownTarget, logger);
        string result;
        result = this.ScaleDownTargetExpression.Invoke(dto);
        return result;
    }

    public bool ScaleUpConditionMethod(MetricEvalDto dto, ILogger logger)
    {
        logger.LogTrace($"ScaleUpCondition evaluation {this.ScaleUpCondition}");
        this.LogRuleDataSample(dto, this.ScaleUpCondition, logger);
        bool result;
        result = this.ScaleUpConditionExpression.Invoke(dto);
        return result;
    }

    public bool ScaleDownConditionMethod(MetricEvalDto dto, ILogger logger)
    {
        logger.LogTrace($"ScaleDownCondition evaluation {this.ScaleDownCondition}");
        this.LogRuleDataSample(dto, this.ScaleDownCondition, logger);
        bool result;
        result = this.ScaleDownConditionExpression.Invoke(dto);
        return result;
    }

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
    /// Do not scale up again until this amount of time has passed
    /// </summary>
    public int ScaleUpCooldownSeconds { get; set; }

    /// <summary>
    /// Do not scale down again until this amount of time has passed
    /// </summary>
    public int ScaleDownCooldownSeconds { get; set; }
}

public class Metric
{
    public string Id { get; set; }

    public string Name { get; set; }

    public string ResourceId { get; set; }

    public string SplitName { get; set; }

    public string SplitValue { get; set; }

    public string Window { get; set; }

    public string TimeGrain { get; set; }

    public List<string> Aggregations { get; set; }

    public List<MetricAggregationType> ParsedAggregations = null;

    public Func<MetricEvalDtoResultValue, MetricEvalDtoResultValue> TransformExpression { get; set; }

    public string Transform { get; set; }

    /// <summary>
    /// When true, allows the metric to fail to load without throwing an exception. 
    /// Instead, a debug message will be logged. Defaults to false.
    /// Useful when a metric may not be available for certain resource configurations (e.g., dtu_consumption_percent is not available for VCore model SQL databases).
    /// </summary>
    public bool AllowFail { get; set; } = false;
}
