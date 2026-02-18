using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using Azure.Core;
using Azure.Monitor.Query;
using Azure.Monitor.Query.Models;
using Azure.ResourceManager;
using Microsoft.Extensions.Logging;
using poolautoscaler.configuration;
using poolautoscaler.metrics.Dto;
using poolautoscaler.resourcemanagement;

namespace poolautoscaler.metrics
{
    /// <summary>
    /// Default metrics gatherer using Azure Monitor and custom metrics.
    /// </summary>
    internal sealed class AzureMonitorMetricsGatherer : IMetricsGatherer
    {
        private readonly TokenCredential credential;
        private readonly ArmClient armClient;
        private readonly ConcurrentDictionary<string, MetricForecastResult> forecastCache = new ConcurrentDictionary<string, MetricForecastResult>();

        /// <summary>
        /// Initializes a new instance of the <see cref="AzureMonitorMetricsGatherer"/> class.
        /// </summary>
        /// <param name="armClient">The ARM client.</param>
        /// <param name="credential">The token credential.</param>
        public AzureMonitorMetricsGatherer(ArmClient armClient, TokenCredential credential)
        {
            this.armClient = armClient;
            this.credential = credential;
        }

        /// <inheritdoc/>
        public async Task<Dictionary<string, MetricEvalDtoResult>> GatherMetricsAsync(
            ScalingConfiguration setting,
            ResourceState state,
            CancellationToken cancellationToken,
            ILogger logger)
        {
            var metrics = new Dictionary<string, MetricEvalDtoResult>();
            var metricsClient = new MetricsQueryClient(this.credential, new MetricsQueryClientOptions(MetricsQueryClientOptions.ServiceVersion.V2018_01_01));
            var eval = new MetricEvaluation(logger);

            if (setting.Metrics != null)
            {
                foreach (var metric in setting.Metrics.Values)
                {
                    if (metric.Name.StartsWith("custom_"))
                    {
                        metrics.Add(
                            metric.Id,
                            await state.CustomMetric(this.armClient, this.credential, cancellationToken, setting, metric.Name));
                        continue;
                    }

                    var metricWindow = TimeSpan.Parse(metric.Window);
                    var metricTimeGrain = TimeSpan.Parse(metric.TimeGrain ?? "00:01");
                    var targetResource = metric.ResourceId ?? state.Resource.Id;
                    var metricNamespace = string.IsNullOrWhiteSpace(metric.Namespace) ? null : metric.Namespace;
                    string splitName = metric.SplitName;
                    string splitValue = metric.SplitValue;

                    targetResource = state.ReplaceResourceParts(targetResource);
                    metricNamespace = metricNamespace == null ? null : state.ReplaceResourceParts(metricNamespace);
                    splitName = state.ReplaceResourceParts(splitName);
                    splitValue = state.ReplaceResourceParts(splitValue);

                    List<MetricAggregationType> aggregations = new List<MetricAggregationType>() { MetricAggregationType.Average };
                    if (metric.ParsedAggregations != null)
                    {
                        aggregations = metric.ParsedAggregations;
                    }

                    if (!aggregations.Any())
                    {
                        throw new Exception(
                            "Empty metric aggregation types. Aggregations should be explicitly set to avoid mismatch between rules and metric data.");
                    }

                    logger.LogTrace(
                        "Metrics query: Name={Name}, SplitName={SplitName}, SplitValue={SplitValue}, Aggregations={Aggregations}, TargetResource={TargetResource}, TimeRange={Hours}h",
                        metric.Name,
                        splitName,
                        splitValue,
                        string.Join(", ", aggregations),
                        targetResource,
                        Math.Round(metricWindow.TotalHours, 2));

                    MetricEvalDtoResult metricResult;
                    try
                    {
                        metricResult = await eval.RetrieveHistory(
                            metricsClient,
                            targetResource,
                            metric.Name,
                            metricWindow,
                            metricTimeGrain,
                            cancellationToken,
                            splitName,
                            splitValue,
                            aggregations,
                            metricNamespace: metricNamespace);
                    }
                    catch (Azure.RequestFailedException ex) when (ex.Status == 400)
                    {
                        if (metric.AllowFail)
                        {
                            logger.LogDebug("Failed to load metric configuration (allowed as per configuration). {Message}", ex.Message);
                            continue;
                        }

                        ExceptionDispatchInfo.Capture(ex).Throw();
                        throw;
                    }

                    metricResult.Values.Reverse();

                    var originalCount = metricResult.Values.Count;
                    metricResult.Values = metricResult.Values.SkipWhile(v => !v.HasData()).ToList();
                    var removedCount = originalCount - metricResult.Values.Count;
                    if (removedCount > 1)
                    {
                        logger.LogDebug("Removed {Removed} data points from a total of {Total} without data from the beginning of the time series for {Name}. This is not necessarily bad. Review your metrics configuration.", removedCount, originalCount, metric.Name);
                    }

                    metricResult.Values = metricResult.Values.Select((i) => metric.TransformExpression(i)).ToList();

                    if (metricResult.Values.Any() && (metric.ValidValueMin.HasValue || metric.ValidValueMax.HasValue))
                    {
                        foreach (var value in metricResult.Values)
                        {
                            if (value.Default.HasValue)
                            {
                                if (metric.ValidValueMin.HasValue && value.Default.Value < metric.ValidValueMin.Value)
                                {
                                    value.Valid = false;
                                    value.InvalidReason = $"Value {value.Default.Value:F2} is below minimum valid value {metric.ValidValueMin.Value:F2}";
                                }
                                else if (metric.ValidValueMax.HasValue && value.Default.Value > metric.ValidValueMax.Value)
                                {
                                    value.Valid = false;
                                    value.InvalidReason = $"Value {value.Default.Value:F2} is above maximum valid value {metric.ValidValueMax.Value:F2}";
                                }
                            }
                        }

                        var invalidValues = metricResult.Values.Where(v => !v.Valid).ToList();
                        if (invalidValues.Any())
                        {
                            metricResult.Valid = false;
                            metricResult.InvalidReason = $"{invalidValues.Count} out of {metricResult.Values.Count} data points are invalid (e.g., {invalidValues.First().InvalidReason} at {invalidValues.First().TimeStamp:yyyy-MM-dd HH:mm:ss})";
                            logger.LogWarning("Metric '{Name}' appears broken or unreliable: {Reason}", metric.Name, metricResult.InvalidReason);
                        }
                    }

                    metrics.Add(metric.Id, metricResult);

                    if (metric.ForecastEnable)
                    {
                        MetricEvalDtoResult? forecastResult = await state.GetMetricForecast(metric, setting, this.armClient, this.credential, cancellationToken);
                        if (forecastResult == null)
                        {
                            var forecastService = new MetricForecastService(logger);
                            var cacheKey = $"{state.Resource.Id}|{setting.Id}|{metric.Id}";
                            var now = DateTime.UtcNow;

                            if (this.forecastCache.TryGetValue(cacheKey, out var cached) && cached.ExpiresAtUtc > now)
                            {
                                forecastResult = forecastService.GetCurrentForecastValue(cached, new DateTimeOffset(now, TimeSpan.Zero));
                            }
                            else
                            {
                                var fullForecast = await forecastService.GetFullForecastAsync(state, metric, setting, metricsClient, cancellationToken);
                                if (fullForecast != null)
                                {
                                    this.forecastCache[cacheKey] = fullForecast;
                                    forecastResult = forecastService.GetCurrentForecastValue(fullForecast, new DateTimeOffset(now, TimeSpan.Zero));
                                }
                            }
                        }

                        if (forecastResult != null)
                        {
                            metrics.Add(metric.Id + "_forecast", forecastResult);
                        }
                    }
                }
            }

            foreach (var metric in metrics.Values)
            {
                foreach (var metricValue in metric.Values)
                {
                    metricValue.Default = metric.PrimaryAggregation switch
                    {
                        MetricAggregationType.Average => metricValue.Average,
                        MetricAggregationType.Maximum => metricValue.Maximum,
                        MetricAggregationType.Minimum => metricValue.Minimum,
                        MetricAggregationType.Total => metricValue.Total,
                        MetricAggregationType.Count => metricValue.Count,
                        null => metricValue.Average ?? metricValue.Maximum ?? metricValue.Minimum ?? metricValue.Total ?? metricValue.Count
                    };
                }
            }

            return metrics;
        }
    }
}
