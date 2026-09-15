using Azure.Monitor.Query.Models;
using Microsoft.Extensions.Logging;
using poolautoscaler.metrics;
using poolautoscaler.metrics.Dto;
using poolautoscaler.resourcemanagement;
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

        /// <summary>
        /// Default namespace for custom metrics pushed to Azure Monitor (optional).
        /// If not set, defaults to "Custom Autoscaler". Can be overridden per metric.
        /// </summary>
        public string? CustomMetricsNamespace { get; set; }

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

                if (resource.Resources != null)
                {
                    foreach (var resourceInstance in resource.Resources.Values)
                    {
                        if (!string.IsNullOrEmpty(resourceInstance.ResourceFilter))
                        {
                            resourceInstance.ResourceFilterExpression =
                                (Func<ResourceFilterContext, bool>)ExpressionParserUtils.ParseExpression(
                                    resourceInstance.ResourceFilter,
                                    "r",
                                    typeof(ResourceFilterContext),
                                    typeof(bool),
                                    1);
                        }
                    }
                }

                if (resource.CustomMetrics != null)
                {
                    foreach (var customMetric in resource.CustomMetrics)
                    {
                        this.PrepareAndValidateCustomMetric(resource, customMetric);
                    }
                }

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

        /// <summary>
        /// Validates one CustomMetrics row: Query XOR DataExpression, Name only for DataExpression, Query only on SQL resource IDs.
        /// </summary>
        /// <param name="resource">The resource that owns the row.</param>
        /// <param name="customMetric">The CustomMetrics row to validate.</param>
        private void PrepareAndValidateCustomMetric(Resource resource, CustomMetricConfig customMetric)
        {
            var hasQuery = !string.IsNullOrWhiteSpace(customMetric.Query);
            var hasDataExpression = !string.IsNullOrWhiteSpace(customMetric.DataExpression);

            if (hasQuery && hasDataExpression)
            {
                throw new Exception("Custom metric must have exactly one of Query or DataExpression.");
            }

            if (!hasQuery && !hasDataExpression)
            {
                throw new Exception("Custom metric must have a Query or a DataExpression.");
            }

            customMetric.FrequencyParsed = DurationParser.ParseDuration(
                string.IsNullOrWhiteSpace(customMetric.Frequency) ? "5m" : customMetric.Frequency);

            if (hasQuery)
            {
                this.RejectQueryUnlessAllInstanceIdsAreSql(resource);
                SqlQueryConnectionAttributes.RejectReserved(customMetric.QueryConnection);
                if (this.ResourceHasAzureSqlQueryInstance(resource))
                {
                    SqlQueryConnectionAttributes.RequireAzureSqlApplicationIntent(customMetric.QueryConnection);
                }

                customMetric.QueryTimeoutParsed = DurationParser.ParseDuration(
                    string.IsNullOrWhiteSpace(customMetric.QueryTimeout) ? "30s" : customMetric.QueryTimeout);
                return;
            }

            if (customMetric.QueryConnection != null && customMetric.QueryConnection.Count > 0)
            {
                throw new Exception("QueryConnection is only valid on Query CustomMetrics rows.");
            }

            if (string.IsNullOrWhiteSpace(customMetric.Name))
            {
                throw new Exception("Custom metric must have a Name.");
            }

            customMetric.DataExpressionDelegate =
                (Func<CustomMetricDataContext, double>)ExpressionParserUtils.ParseExpression(
                    customMetric.DataExpression,
                    "data",
                    typeof(CustomMetricDataContext),
                    typeof(double),
                    1);
        }

        /// <summary>True when any instance ResourceId is Azure SQL Database or Elastic Pool.</summary>
        /// <param name="resource">The resource whose instance IDs are checked.</param>
        /// <returns>True when ApplicationIntent must be set on QueryConnection.</returns>
        private bool ResourceHasAzureSqlQueryInstance(Resource resource)
        {
            if (resource.Resources == null)
            {
                return false;
            }

            foreach (var instance in resource.Resources.Values)
            {
                if (ResourceStateFactory.IsAzureSqlQueryResourceId(instance.ResourceId))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Rejects Query unless every instance ResourceId on the resource matches one of the four SQL factory regexes.
        /// </summary>
        /// <param name="resource">The resource whose instance IDs are checked.</param>
        private void RejectQueryUnlessAllInstanceIdsAreSql(Resource resource)
        {
            if (resource.Resources == null || resource.Resources.Count == 0)
            {
                throw new Exception("Custom metric Query is only allowed on Azure SQL Database, Azure SQL Elastic Pool, PostgreSQL Flexible Server, or MySQL Flexible Server.");
            }

            foreach (var instance in resource.Resources.Values)
            {
                if (!ResourceStateFactory.IsSqlQueryResourceId(instance.ResourceId))
                {
                    throw new Exception(
                        $"Custom metric Query is not allowed on resource '{instance.ResourceId}'. Query is only allowed on Azure SQL Database, Azure SQL Elastic Pool, PostgreSQL Flexible Server, or MySQL Flexible Server.");
                }
            }
        }
    }
}
