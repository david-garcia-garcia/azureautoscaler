using System.Runtime.ExceptionServices;
using Azure.Core;
using Azure.Monitor.Query;
using Azure.Monitor.Query.Models;
using Azure.ResourceManager;
using Microsoft.Extensions.Logging;
using poolautoscaler.configuration;
using poolautoscaler.dimensions;
using poolautoscaler.licensing;
using poolautoscaler.metrics;
using poolautoscaler.strategies;
using poolautoscaler.metrics.Dto;
using poolautoscaler.utils;

namespace poolautoscaler.resourcemanagement
{
    /// <summary>
    /// Processes discovered resources: evaluates scaling rules and applies dimension changes.
    /// Encapsulates the run loop and metrics gathering for testability.
    /// </summary>
    public class ResourceProcessor
    {
        private readonly ILogger logger;
        private readonly IReadOnlyList<IDimension> dimensions;
        private readonly TokenCredential credential;
        private readonly ArmClient armClient;
        private readonly LicenseInfo licenseInfo;
        private readonly Func<DateTime> utcNowProvider;

        /// <summary>
        /// Initializes a new instance of the <see cref="ResourceProcessor"/> class.
        /// </summary>
        /// <param name="logFactory">The logger factory.</param>
        /// <param name="dimensions">The list of dimension handlers.</param>
        /// <param name="credential">The token credential for Azure and metrics.</param>
        /// <param name="armClient">The ARM client.</param>
        /// <param name="licenseInfo">The license information.</param>
        /// <param name="utcNowProvider">Optional. Provides current UTC time for TimeWindow evaluation. Defaults to <see cref="DateTime.UtcNow"/>.</param>
        internal ResourceProcessor(
            ILoggerFactory logFactory,
            IReadOnlyList<IDimension> dimensions,
            TokenCredential credential,
            ArmClient armClient,
            LicenseInfo licenseInfo,
            Func<DateTime>? utcNowProvider = null)
        {
            this.logger = logFactory.CreateLogger("ResourceProcessor");
            this.dimensions = dimensions;
            this.credential = credential;
            this.armClient = armClient;
            this.licenseInfo = licenseInfo;
            this.utcNowProvider = utcNowProvider ?? (() => DateTime.UtcNow);
        }

        /// <summary>
        /// Processes a single resource: checks enabled and evaluation timing, then runs the scaling loop.
        /// Call this from the main loop for each resource; license limit and loop iteration stay in the caller.
        /// </summary>
        /// <param name="resourceState">The resource to process.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if the scaling loop was run (caller may use this to count processed resources for license limit).</returns>
        internal async Task<bool> ProcessOneAsync(
            ResourceState resourceState,
            CancellationToken cancellationToken = default)
        {
            if (resourceState.Configuration.Enabled == false)
            {
                return false;
            }

            if (resourceState.NextEvaluationSeconds() > 0)
            {
                return false;
            }

            var ran = false;
            try
            {
                await this.RunLoop(resourceState, cancellationToken);
                ran = true;
            }
            catch (ResourceNotFoundException ex)
            {
                this.logger.LogWarning("Resource '{ResourceId}' no longer exists in Azure and will be skipped.", ex.ResourceId);
            }
            catch (Exception ex)
            {
                resourceState.Logger.LogError(ex, ex.Message);
                resourceState.Logger.LogWarning("Resource evaluation will be disabled for 1 hour."); // Hardcoded right now
                resourceState.DisabledUntil["Unhandled exception: " + ex.Message] = DateTime.UtcNow.AddHours(1);
            }
            finally
            {
                resourceState?.ResetEvaluation();
            }

            resourceState?.Logger.LogDebug("Next evaluation in {Interval}", TimeSpan.FromSeconds(resourceState.NextEvaluationSeconds()).ToString("g"));
            return ran;
        }

        private async Task RunLoop(ResourceState state, CancellationToken stoppingToken)
        {
            var logger = state.Logger;
            var finder = new ConfigFinder();

            var utcNow = this.utcNowProvider();
            var scalingConfigurations = (from p in state.Configuration.ScalingConfigurations.Values
                                         where finder.SettingIsActive(p, utcNow)
                                         select p).ToList();

            if (!scalingConfigurations.Any())
            {
                logger.LogTrace("No scaling configurations apply right now.");
                return;
            }

            logger.LogTrace("The following ScalingConfigurations are active and will be evaluated: {Ids}", string.Join(", ", scalingConfigurations.Select((i) => i.Id)));

            await state.Refresh(this.armClient, this.credential, stoppingToken);

            logger.LogDebug("Existing object state {State}", HelperExtensions.SerializeSimple(state.ExistingStateRaw));

            if (state.IsDisabled())
            {
                if ((DateTime.UtcNow - state.LastDisabledMessageLogged).TotalHours >= 1)
                {
                    logger.LogInformation("Resource is currently disabled: {Reasons}", string.Join(", ", state.DisabledUntil.Keys));
                    state.LastDisabledMessageLogged = DateTime.UtcNow;
                }

                return;
            }

            state.LastDisabledMessageLogged = DateTime.MinValue;
            var capturingLogger = new CapturingLogger(logger);

            foreach (var setting in scalingConfigurations)
            {
                var metricsClient = new MetricsQueryClient(this.credential, new MetricsQueryClientOptions(MetricsQueryClientOptions.ServiceVersion.V2018_01_01));

                Dictionary<string, MetricEvalDtoResult> metrics = await this.GatherMetrics(
                    setting,
                    state,
                    metricsClient,
                    stoppingToken,
                    capturingLogger);

                foreach (var metricEntry in metrics)
                {
                    var metricId = metricEntry.Key;
                    var metricResult = metricEntry.Value;

                    if (!metricResult.Values.Any())
                    {
                        capturingLogger.LogDebug("Metric={MetricId}, valid={Valid}, values=0, aggregation=none, valuedetail=<empty>", metricId, metricResult.Valid);
                        continue;
                    }

                    var aggregationType = metricResult.Values.First().GetAggregationType();
                    var valuesDetail = string.Join(",", metricResult.Values.Select(v => v.RenderValueWithStatus()));
                    var invalidReason = metricResult.Valid ? string.Empty : $", reason=\"{metricResult.InvalidReason}\"";
                    capturingLogger.LogDebug(
                        "Metric={MetricId}, valid={Valid}, values={Count}, aggregation={Aggregation}, valuedetail={Detail}{InvalidReason}",
                        metricId,
                        metricResult.Valid,
                        metricResult.Values.Count,
                        aggregationType,
                        valuesDetail,
                        invalidReason);
                }

                capturingLogger.LogTrace("Evaluating scale configuration {Id}", setting.Id);

                var invalidMetrics = metrics.Where(m => !m.Value.Valid).ToList();
                if (invalidMetrics.Any())
                {
                    var reasons = string.Join("; ", invalidMetrics.Select(m => $"{m.Key}: {m.Value.InvalidReason}"));
                    capturingLogger.LogWarning("Skipping scaling configuration '{Id}' because one or more metrics are invalid: {Reasons}", setting.Id, reasons);
                    continue;
                }

                foreach (var rule in setting.ScalingRules.Values)
                {
                    IDimension? dimension = null;

                    foreach (var dim in this.dimensions)
                    {
                        if (dim.CanApplyDimension(state, rule, capturingLogger))
                        {
                            dimension = dim;
                            break;
                        }
                    }

                    if (dimension == null)
                    {
                        capturingLogger.LogDebug("No dimension handler compatible with dimension '{Dimension}' found in this resource. Rule: '{RuleId}'", rule.Dimension, rule.Id);
                        continue;
                    }

                    capturingLogger.LogDebug("Current request state {State}", HelperExtensions.SerializeSimple(state.RequestedStateRaw));
                    capturingLogger.LogTrace("Evaluating rule {Id}", rule.Id);

                    dimension.ValidateRuleConfiguration(rule);

                    var strategy = GetRuleStrategy(rule);
                    var currentDimensionValue = dimension.GetCurrentDimensionValue(state);
                    var targetDimensionValue = await strategy.EvaluateTargetDimensionValue(rule, dimension, state, capturingLogger, this.credential, stoppingToken, metrics);

                    if (dimension.Compare(state.Resource, targetDimensionValue, currentDimensionValue) == -1)
                    {
                        if (state.LastScale != null && (DateTime.UtcNow - state.LastScale).Value.TotalSeconds < rule.ScaleDownCooldownSeconds)
                        {
                            capturingLogger.LogTrace(
                                "Skipping scale down from {Current} to {Target} because ScaleDownCooldownSeconds {Seconds}s have not yet passed.",
                                currentDimensionValue,
                                targetDimensionValue,
                                rule.ScaleDownCooldownSeconds);
                            continue;
                        }

                        if (setting.ScaleDownLockWindowMinutes.HasValue && DateTime.UtcNow.Minute < setting.ScaleDownLockWindowMinutes)
                        {
                            capturingLogger.LogTrace(
                                "Skipping scale down from {Current} to {Target} not allowed before minute {Minute} of a billable hour.",
                                currentDimensionValue,
                                targetDimensionValue,
                                setting.ScaleDownLockWindowMinutes);
                            continue;
                        }
                    }

                    if (dimension.Compare(state.Resource, targetDimensionValue, currentDimensionValue) == 1)
                    {
                        if (state.LastScale != null && (DateTime.UtcNow - state.LastScale).Value.TotalSeconds < rule.ScaleUpCooldownSeconds)
                        {
                            capturingLogger.LogTrace(
                                "Skipping scale up from {Current} to {Target} because ScaleUpCooldownSeconds {Seconds}s have not yet passed.",
                                currentDimensionValue,
                                targetDimensionValue,
                                rule.ScaleUpCooldownSeconds);
                            continue;
                        }

                        if (setting.ScaleDownLockWindowMinutes.HasValue && DateTime.UtcNow.Minute > setting.ScaleUpAllowWindowMinutes)
                        {
                            capturingLogger.LogTrace(
                                "Skipping scale up from {Current} to {Target} not allowed after minute {Minute} of a billable hour.",
                                currentDimensionValue,
                                targetDimensionValue,
                                setting.ScaleUpAllowWindowMinutes);
                            continue;
                        }
                    }

                    var existingDimensionRequest = dimension.GetRequestedDimensionValue(state);

                    if (this.licenseInfo.IsRestricted)
                    {
                        capturingLogger.LogInformation("License {Reason}: Adding 1 minute delay to scaling operation", this.licenseInfo.Reason);
                        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                    }

                    await dimension.SetDimensionValue(stoppingToken, state, capturingLogger, this.credential, targetDimensionValue);

                    var newDimensionRequest = dimension.GetRequestedDimensionValue(state);

                    if (existingDimensionRequest != newDimensionRequest)
                    {
                        capturingLogger.LogDebug("Rule '{RuleId}' Dimension request changed from {From} to {To}", rule.Id, existingDimensionRequest ?? "(null)", newDimensionRequest);
                    }
                    else
                    {
                        capturingLogger.LogDebug("Rule '{RuleId}' requested target value '{Value}'", rule.Id, targetDimensionValue);
                    }
                }
            }

            var patchOperation = state.PreparePatch();

            if (patchOperation.HasChanges)
            {
                capturingLogger.Replay(LogLevel.Information, LogLevel.Debug);

                logger.LogInformation("Existing resource state {State}", HelperExtensions.SerializeSimple(state.ExistingStateRaw));
                logger.LogInformation("Target resource state: {State}", HelperExtensions.SerializeSimple(patchOperation.PatchData));
                logger.LogInformation("Patch operation disruptive: {Disruptive}", patchOperation.Disruptive ? "yes" : "no");

                if (state.Configuration.WhatIf == true)
                {
                    logger.LogInformation("Patching Resource (WHATIF)");
                }
                else
                {
                    await state.ApplyChanges(patchOperation, stoppingToken);
                }

                state.LastScale = DateTime.UtcNow;
            }
            else
            {
                logger.LogDebug("No changes in patch operation");
                logger.LogDebug("Target resource state: {State}", HelperExtensions.SerializeSimple(patchOperation.PatchData));
            }

            capturingLogger.Clear();
        }

        private async Task<Dictionary<string, MetricEvalDtoResult>> GatherMetrics(
            ScalingConfiguration setting,
            ResourceState state,
            MetricsQueryClient metricsClient,
            CancellationToken stoppingToken,
            ILogger logger)
        {
            var metrics = new Dictionary<string, MetricEvalDtoResult>();
            var eval = new MetricEvaluation(logger);

            if (setting.Metrics != null)
            {
                foreach (var metric in setting.Metrics.Values)
                {
                    if (metric.Name.StartsWith("custom_"))
                    {
                        metrics.Add(
                            metric.Id,
                            await state.CustomMetric(this.armClient, this.credential, stoppingToken, setting, metric.Name));
                        continue;
                    }

                    var metricWindow = TimeSpan.Parse(metric.Window);
                    var metricTimeGrain = TimeSpan.Parse(metric.TimeGrain ?? "00:01");
                    var targetResource = metric.ResourceId ?? state.Resource.Id;
                    string splitName = metric.SplitName;
                    string splitValue = metric.SplitValue;

                    targetResource = state.ReplaceResourceParts(targetResource);
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
                            stoppingToken,
                            splitName,
                            splitValue,
                            aggregations);
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

        private static IRuleStrategy GetRuleStrategy(ScalingRule rule)
        {
            if (rule.ScalingStrategy == "Fixed")
            {
                return new RuleStrategyFixed();
            }

            if (rule.ScalingStrategy == "Autoadjust")
            {
                return new RuleStrategyAutoAdjust();
            }

            throw new ArgumentException($"Invalid scaling strategy: '{rule.ScalingStrategy}'");
        }
    }
}
