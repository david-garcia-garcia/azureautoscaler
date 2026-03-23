using System.Diagnostics;
using Azure.Core;
using Microsoft.Extensions.Logging;
using poolautoscaler.configuration;
using poolautoscaler.dimensions;
using poolautoscaler.licensing;
using poolautoscaler.metrics;
using poolautoscaler.metrics.Dto;
using poolautoscaler.strategies;
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
        private readonly IArmClientWrapper armClientWrapper;
        private readonly LicenseInfo licenseInfo;
        private readonly Func<DateTime> utcNowProvider;
        private readonly IMetricsGatherer metricsGatherer;
        private readonly CustomMetricsPusher customMetricsPusher;

        /// <summary>
        /// Initializes a new instance of the <see cref="ResourceProcessor"/> class.
        /// </summary>
        /// <param name="logFactory">The logger factory.</param>
        /// <param name="dimensions">The list of dimension handlers.</param>
        /// <param name="credential">The token credential for Azure and metrics.</param>
        /// <param name="armClientWrapper">The ARM client wrapper (provides client and cached tenant).</param>
        /// <param name="licenseInfo">The license information.</param>
        /// <param name="resourceLocationResolver">Resolves resource IDs to region for custom metrics.</param>
        /// <param name="utcNowProvider">Optional. Provides current UTC time for TimeWindow evaluation. Defaults to <see cref="DateTime.UtcNow"/>.</param>
        /// <param name="metricsGatherer">Optional. Gathers metrics for evaluation. Defaults to <see cref="AzureMonitorMetricsGatherer"/>.</param>
        /// <param name="defaultCustomMetricsNamespace">Optional global default namespace for custom metrics.</param>
        internal ResourceProcessor(
            ILoggerFactory logFactory,
            IReadOnlyList<IDimension> dimensions,
            TokenCredential credential,
            IArmClientWrapper armClientWrapper,
            LicenseInfo licenseInfo,
            IResourceLocationResolver resourceLocationResolver,
            Func<DateTime>? utcNowProvider = null,
            IMetricsGatherer? metricsGatherer = null,
            string? defaultCustomMetricsNamespace = null)
        {
            this.logger = logFactory.CreateLogger("ResourceProcessor");
            this.dimensions = dimensions;
            this.credential = credential;
            this.armClientWrapper = armClientWrapper;
            this.licenseInfo = licenseInfo;
            this.utcNowProvider = utcNowProvider ?? (() => DateTime.UtcNow);
            this.metricsGatherer = metricsGatherer ?? new AzureMonitorMetricsGatherer(armClientWrapper.Client, credential);
            this.customMetricsPusher = new CustomMetricsPusher(credential, armClientWrapper.Client, this.logger, resourceLocationResolver, defaultCustomMetricsNamespace);
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
                resourceState.Logger.LogWarning("Resource evaluation will be disabled for 1 hour.");
                resourceState.DisabledUntil[ResourceState.UnhandledExceptionPrefix + ex.Message] = DateTime.UtcNow.Add(ResourceState.UnhandledExceptionDisableDuration);
            }
            finally
            {
                resourceState?.ResetEvaluation();
            }

            resourceState?.Logger.LogDebug("Next evaluation in {Interval}", TimeSpan.FromSeconds(resourceState.NextEvaluationSeconds()).ToString("g"));
            return ran;
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

        /// <summary>Formats the configured DimensionValueMin/Max as a mathematical range suffix for logging, e.g. " (valid range: value ∈ [50, 200])".</summary>
        private static string FormatDimensionRangeSuffix(ScalingRule rule)
        {
            var hasMin = !string.IsNullOrEmpty(rule.DimensionValueMin);
            var hasMax = !string.IsNullOrEmpty(rule.DimensionValueMax);
            if (!hasMin && !hasMax)
            {
                return string.Empty;
            }

            string interval;
            if (hasMin && hasMax)
            {
                interval = $"[{rule.DimensionValueMin}, {rule.DimensionValueMax}]";
            }
            else if (hasMin)
            {
                interval = $"[{rule.DimensionValueMin}, NaN)";
            }
            else
            {
                interval = $"(NaN, {rule.DimensionValueMax}]";
            }

            return $" (valid range: {interval})";
        }

        /// <summary>
        /// Determines the effective last scale time for a specific rule.
        ///
        /// Candidates (newest wins):
        /// - Last change detected in the configured LastScaleMetric time series (if any).
        /// - Rule-level LastScale (remembered from previous evaluations).
        /// - Resource-level LastScale on the underlying state.
        ///
        /// This ensures that even when ARM history/metrics no longer cover the original scale
        /// moment, the rule still has a monotonic notion of "last scale" for cooldowns.
        /// </summary>
        private DateTime GetLastScaleTimeForRule(
            ResourceState state,
            ScalingRule rule,
            Dictionary<string, MetricEvalDtoResult> metrics,
            ILogger logger)
        {
            // We know this is non-null from the RunLoop guard.
            var candidate = state.LastScale!.Value;

            // 1) Metric-based candidate: last time the tracked metric changed.
            DateTime? metricLastChange = null;

            if (!string.IsNullOrWhiteSpace(rule.LastScaleMetric) &&
                metrics.TryGetValue(rule.LastScaleMetric, out var lastScaleMetricResult) &&
                lastScaleMetricResult.Values != null &&
                lastScaleMetricResult.Values.Any())
            {
                var orderedValues = lastScaleMetricResult.Values
                    .OrderBy(v => v.TimeStamp)
                    .ToList();

                double? lastValue = null;

                foreach (var value in orderedValues)
                {
                    var current = value.Default
                                  ?? value.Average
                                  ?? value.Maximum
                                  ?? value.Minimum
                                  ?? value.Total
                                  ?? value.Count;

                    if (current == null)
                    {
                        continue;
                    }

                    if (lastValue == null)
                    {
                        lastValue = current;
                        continue;
                    }

                    if (Math.Abs(current.Value - lastValue.Value) > double.Epsilon)
                    {
                        metricLastChange = value.TimeStamp.UtcDateTime;
                        lastValue = current;
                    }
                }

                if (metricLastChange.HasValue)
                {
                    logger.LogDebug(
                        "Rule '{RuleId}' using LastScaleMetric '{MetricId}' with last change timestamp {Timestamp}",
                        rule.Id,
                        rule.LastScaleMetric,
                        metricLastChange.Value);
                }
                else
                {
                    logger.LogDebug(
                        "Rule '{RuleId}' specified LastScaleMetric '{MetricId}' but no value changes were detected in current window.",
                        rule.Id,
                        rule.LastScaleMetric);
                }
            }
            else if (!string.IsNullOrWhiteSpace(rule.LastScaleMetric))
            {
                logger.LogWarning(
                    "Rule '{RuleId}' specified LastScaleMetric '{MetricId}' but metric not found or has no values in current window.",
                    rule.Id,
                    rule.LastScaleMetric);
            }

            // 2) Rule-level remembered LastScale.
            if (rule.LastScale.HasValue && rule.LastScale.Value > candidate)
            {
                candidate = rule.LastScale.Value;
            }

            // 3) Metric-based candidate, if newer than what we have so far.
            if (metricLastChange.HasValue && metricLastChange.Value > candidate)
            {
                candidate = metricLastChange.Value;
            }

            // Persist back to the rule so we have a stable memory across runs.
            rule.LastScale = candidate;

            return candidate;
        }

        private async Task RunLoop(ResourceState state, CancellationToken stoppingToken)
        {
            var logger = state.Logger;
            var finder = new ConfigFinder();

            var utcNow = this.utcNowProvider();
            var scalingConfigurations = (from p in state.Configuration.ScalingConfigurations.Values
                                         where finder.SettingIsActive(p, utcNow)
                                         select p).ToList();

            await state.Refresh(this.armClientWrapper, this.credential, stoppingToken);
            logger.LogDebug("Existing object state {State}", HelperExtensions.SerializeSimple(state.ExistingStateRaw));
            await this.customMetricsPusher.PushIfDueAsync(state, stoppingToken);

            if (!scalingConfigurations.Any())
            {
                logger.LogTrace("No scaling configurations apply right now.");
                return;
            }

            logger.LogTrace("The following ScalingConfigurations are active and will be evaluated: {Ids}", string.Join(", ", scalingConfigurations.Select((i) => i.Id)));

            if (state.IsDisabled())
            {
                if ((DateTime.UtcNow - state.LastDisabledMessageLogged).TotalHours >= 1)
                {
                    logger.LogInformation("Resource is currently disabled: {Reasons}", string.Join(", ", state.DisabledUntil.Keys));
                    state.LastDisabledMessageLogged = DateTime.UtcNow;
                }

                return;
            }

            if (state.LastScale == null)
            {
                throw new Exception("Last scale time is not set for resource. Resource has not been initialized.");
            }

            // This is a very sloppy and unreliable metric, but helps. It captures changes
            // made to the resource either internally our externally. Of course a change does not mean
            // that an actual scale operation happened.... but on most operational scenarios it works.
            var defaultLapsedSinceLastScaleOperation = utcNow - state.LastScale.Value;

            state.LastDisabledMessageLogged = DateTime.MinValue;
            var capturingLogger = new CapturingLogger(logger);
            var metricResultsWithDiagnostics = new List<MetricEvalDtoResult>();

            foreach (var setting in scalingConfigurations)
            {
                Dictionary<string, MetricEvalDtoResult> metrics = await this.metricsGatherer.GatherMetricsAsync(
                    setting,
                    state,
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
                    if (metricResult.Diagnostics != null)
                    {
                        // Use resource logger (not capturing) so large forecast tables are not duplicated by Replay on scale.
                        metricResult.EmitDiagnostics(logger, LogLevel.Debug);
                        metricResultsWithDiagnostics.Add(metricResult);
                    }
                }

                capturingLogger.LogDebug(
                    "Evaluating scale configuration {Id}: ScaleDownLockWindowMinutes={ScaleDownLockWindowMinutes}, ScaleUpAllowWindowMinutes={ScaleUpAllowWindowMinutes}, defaultLapsedSinceLastScaleOperation={lapsedSinceLastScaleOperation}",
                    setting.Id,
                    setting.ScaleDownLockWindowMinutes,
                    setting.ScaleUpAllowWindowMinutes,
                    defaultLapsedSinceLastScaleOperation.ToString(@"hh\:mm\:ss"));

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
                    dimension.ValidateRuleConfiguration(rule);

                    var lastScaleTimeForRule = this.GetLastScaleTimeForRule(state, rule, metrics, capturingLogger);
                    var lapsedSinceLastScaleOperation = utcNow - lastScaleTimeForRule;

                    var strategy = GetRuleStrategy(rule);
                    var currentDimensionValue = dimension.GetCurrentDimensionValue(state);
                    var targetDimensionValue = await strategy.EvaluateTargetDimensionValue(rule, dimension, state, capturingLogger, this.credential, stoppingToken, metrics);

                    var isScaleDown = dimension.Compare(state.Resource, targetDimensionValue, currentDimensionValue) == -1;
                    var isScaleUp = dimension.Compare(state.Resource, targetDimensionValue, currentDimensionValue) == 1;

                    capturingLogger.LogDebug("Rule '{0}' evaluated: ScaleDownCooldownSeconds={1}, ScaleUpCooldownSeconds={2}, isScaleDown={3}, isScaleUp={4}, lapsedSinceLastScaleOperation={5}", rule.Id, rule.ScaleDownCooldownSeconds, rule.ScaleUpCooldownSeconds, isScaleDown, isScaleUp, lapsedSinceLastScaleOperation.ToString(@"hh\:mm\:ss"));

                    if (isScaleDown)
                    {
                        if (lapsedSinceLastScaleOperation.TotalSeconds < rule.ScaleDownCooldownSeconds)
                        {
                            capturingLogger.LogTrace(
                                "{ruleId} Skipping scale down from {Current} to {Target} because ScaleDownCooldownSeconds {Seconds}s have not yet passed.",
                                rule.Id,
                                currentDimensionValue,
                                targetDimensionValue,
                                rule.ScaleDownCooldownSeconds);
                            continue;
                        }

                        if (setting.ScaleDownLockWindowMinutes.HasValue && utcNow.Minute >= setting.ScaleDownLockWindowMinutes)
                        {
                            capturingLogger.LogTrace(
                                "{ruleId} Skipping scale down from {Current} to {Target} not allowed from minute {Minute} onward (lock window) of a billable hour.",
                                rule.Id,
                                currentDimensionValue,
                                targetDimensionValue,
                                setting.ScaleDownLockWindowMinutes);
                            continue;
                        }
                    }

                    if (isScaleUp)
                    {
                        if (lapsedSinceLastScaleOperation.TotalSeconds < rule.ScaleUpCooldownSeconds)
                        {
                            capturingLogger.LogTrace(
                                "{ruleId} Skipping scale up from {Current} to {Target} because ScaleUpCooldownSeconds {Seconds}s have not yet passed.",
                                rule.Id,
                                currentDimensionValue,
                                targetDimensionValue,
                                rule.ScaleUpCooldownSeconds);
                            continue;
                        }

                        if (setting.ScaleDownLockWindowMinutes.HasValue && utcNow.Minute > setting.ScaleUpAllowWindowMinutes)
                        {
                            capturingLogger.LogTrace(
                                "{ruleId} Skipping scale up from {Current} to {Target} not allowed after minute {Minute} of a billable hour.",
                                rule.Id,
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

                    var rangeSuffix = FormatDimensionRangeSuffix(rule);

                    capturingLogger.LogDebug(
                        "Rule '{RuleId}' requested '{targetDimensionValue}' resulting in a changed target from {From} to {To}{Range}",
                        rule.Id,
                        targetDimensionValue,
                        existingDimensionRequest ?? "(null)",
                        newDimensionRequest,
                        rangeSuffix);
                }
            }

            var patchOperation = state.PreparePatch();

            if (patchOperation.HasChanges)
            {
                capturingLogger.Replay(LogLevel.Information, LogLevel.Debug);

                foreach (var metricWithDiag in metricResultsWithDiagnostics)
                {
                    metricWithDiag.EmitDiagnostics(logger, LogLevel.Information);
                }

                logger.LogInformation("Existing resource state {State}", HelperExtensions.SerializeSimple(state.ExistingStateRaw));
                logger.LogInformation("Target resource state: {State}", HelperExtensions.SerializeSimple(patchOperation.PatchData));
                logger.LogInformation("Patch operation disruptive: {Disruptive}", patchOperation.Disruptive ? "yes" : "no");

                if (state.Configuration.WhatIf == true)
                {
                    logger.LogInformation("Patching Resource (WHATIF)");
                }
                else
                {
                    state.DisabledUntil[ResourceState.ScaleOperationInProgress] = DateTime.MaxValue;
                    logger.LogInformation("Dispatching scale operation (background)");

                    var patchOp = patchOperation;
                    var getUtcNow = this.utcNowProvider;

                    _ = Task.Run(
                            async () =>
                            {
                                var resState = state;
                                var resLogger = resState.Logger;
                                try
                                {
                                    resLogger.LogInformation("Starting scale operation (background)");
                                    var scaleStopwatch = Stopwatch.StartNew();
                                    await resState.ApplyChanges(patchOp, stoppingToken);
                                    scaleStopwatch.Stop();
                                    resState.LastScale = getUtcNow();
                                    resLogger.LogInformation(
                                        "Scale operation completed successfully after {Elapsed}",
                                        scaleStopwatch.Elapsed.ToString(@"hh\:mm\:ss"));
                                }
                                catch (OperationCanceledException)
                                {
                                    resLogger.LogInformation("Scale operation was cancelled");
                                }
                                catch (Exception ex)
                                {
                                    resLogger.LogError(ex, "Scale operation failed: {Message}", ex.Message);
                                    resState.DisabledUntil[ResourceState.UnhandledExceptionPrefix + ex.Message] = DateTime.UtcNow.Add(ResourceState.UnhandledExceptionDisableDuration);
                                }
                                finally
                                {
                                    resState.DisabledUntil.Remove(ResourceState.ScaleOperationInProgress);
                                }
                            },
                            stoppingToken);
                }
            }
            else
            {
                logger.LogDebug("No changes in patch operation");
                logger.LogDebug("Target resource state: {State}", HelperExtensions.SerializeSimple(patchOperation.PatchData));
            }

            capturingLogger.Clear();
        }
    }
}
