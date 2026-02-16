using Azure.Monitor.Query.Models;
using Microsoft.Extensions.Logging;
using poolautoscaler.metrics.Dto;
using poolautoscaler.strategies.Dto;
using poolautoscaler.utils;

namespace poolautoscaler.configuration
{
    /// <summary>Root configuration (credentials, defaults, resource list).</summary>
    public class Configuration
    {
        /// <summary>Azure credential type (e.g. DefaultAzureCredential, DeviceCodeCredential).</summary>
        public string AzureCredentialType { get; set; }

        /// <summary>Default what-if mode for resources.</summary>
        public bool DefaultResourceWhatIf { get; set; } = true;

        /// <summary>Default enabled state for resources.</summary>
        public bool DefaultResourceEnabled { get; set; } = true;

        /// <summary>Default polling frequency (e.g. "4m").</summary>
        public string DefaultResourceFrequency { get; set; } = "4m";

        /// <summary>How often to discover resources (e.g. "1h").</summary>
        public string ResourceDiscoveryFrequency { get; set; } = "1h";

        /// <summary>Parsed discovery interval.</summary>
        public TimeSpan ResourceDiscoveryFrequencyParsed { get; set; }

        /// <summary>List of resources to manage.</summary>
        public List<Resource> Resources { get; set; }

        /// <summary>Parses defaults and validates resources.</summary>
        /// <param name="logger">The logger.</param>
        public void PrepareAndValidate(ILogger logger)
        {
            this.ResourceDiscoveryFrequencyParsed = DurationParser.ParseDuration(this.ResourceDiscoveryFrequency);

            if (this.Resources == null)
            {
                return;
            }

            foreach (var resource in this.Resources)
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

                if (resource.ScalingConfigurations == null)
                {
                    continue;
                }

                foreach (var scalingConfiguration in resource.ScalingConfigurations)
                {
                    var config = scalingConfiguration.Value;
                    config.Id = scalingConfiguration.Key;

                    if (config.ScalingRules == null)
                    {
                        continue;
                    }

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
                                    (Func<MetricEvalDtoResultValue, MetricEvalDtoResultValue>)ExpressionParserUtils.ParseExpression(
                                        metric.Transform,
                                        "value",
                                        typeof(MetricEvalDtoResultValue),
                                        typeof(MetricEvalDtoResultValue),
                                        1);
                            }
                            else
                            {
                                metric.TransformExpression = a => a;
                            }

                            if (metric.ForecastEnable && !string.IsNullOrEmpty(metric.ForecastTimeRange))
                            {
                                metric.ForecastTimeRangeParsed = DurationParser.ParseDuration(metric.ForecastTimeRange);
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
                                (Func<MetricEvalDto, bool>)ExpressionParserUtils.ParseExpression(
                                    rule.ScaleUpCondition,
                                    "data",
                                    typeof(MetricEvalDto),
                                    typeof(bool),
                                    1);
                        }

                        if (!string.IsNullOrEmpty(rule.ScaleDownCondition))
                        {
                            rule.ScaleDownConditionExpression =
                                (Func<MetricEvalDto, bool>)ExpressionParserUtils.ParseExpression(
                                    rule.ScaleDownCondition,
                                    "data",
                                    typeof(MetricEvalDto),
                                    typeof(bool),
                                    1);
                        }

                        if (!string.IsNullOrEmpty(rule.ScaleTarget))
                        {
                            rule.ScaleTargetExpression =
                                (Func<MetricEvalDto, string>)ExpressionParserUtils.ParseExpression(
                                    rule.ScaleTarget,
                                    "data",
                                    typeof(MetricEvalDto),
                                    typeof(string),
                                    1);
                        }

                        if (!string.IsNullOrEmpty(rule.ScaleUpTarget))
                        {
                            rule.ScaleUpTargetExpression =
                                (Func<MetricEvalDto, string>)ExpressionParserUtils.ParseExpression(
                                    rule.ScaleUpTarget,
                                    "data",
                                    typeof(MetricEvalDto),
                                    typeof(string),
                                    1);
                        }

                        if (!string.IsNullOrEmpty(rule.ScaleDownTarget))
                        {
                            rule.ScaleDownTargetExpression =
                                (Func<MetricEvalDto, string>)ExpressionParserUtils.ParseExpression(
                                    rule.ScaleDownTarget,
                                    "data",
                                    typeof(MetricEvalDto),
                                    typeof(string),
                                    1);
                        }
                    }
                }
            }
        }
    }
}
