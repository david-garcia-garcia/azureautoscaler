using Azure.Core;
using Azure.ResourceManager;
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
        private readonly ArmClient armClient;
        private readonly LicenseInfo licenseInfo;
        private readonly Func<DateTime> utcNowProvider;
        private readonly IMetricsGatherer metricsGatherer;

        /// <summary>
        /// Initializes a new instance of the <see cref="ResourceProcessor"/> class.
        /// </summary>
        /// <param name="logFactory">The logger factory.</param>
        /// <param name="dimensions">The list of dimension handlers.</param>
        /// <param name="credential">The token credential for Azure and metrics.</param>
        /// <param name="armClient">The ARM client.</param>
        /// <param name="licenseInfo">The license information.</param>
        /// <param name="utcNowProvider">Optional. Provides current UTC time for TimeWindow evaluation. Defaults to <see cref="DateTime.UtcNow"/>.</param>
        /// <param name="metricsGatherer">Optional. Gathers metrics for evaluation. Defaults to <see cref="AzureMonitorMetricsGatherer"/>.</param>
        internal ResourceProcessor(
            ILoggerFactory logFactory,
            IReadOnlyList<IDimension> dimensions,
            TokenCredential credential,
            ArmClient armClient,
            LicenseInfo licenseInfo,
            Func<DateTime>? utcNowProvider = null,
            IMetricsGatherer? metricsGatherer = null)
        {
            this.logger = logFactory.CreateLogger("ResourceProcessor");
            this.dimensions = dimensions;
            this.credential = credential;
            this.armClient = armClient;
            this.licenseInfo = licenseInfo;
            this.utcNowProvider = utcNowProvider ?? (() => DateTime.UtcNow);
            this.metricsGatherer = metricsGatherer ?? new AzureMonitorMetricsGatherer(armClient, credential);
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
                interval = $"[{rule.DimensionValueMin}, ∞)";
            }
            else
            {
                interval = $"(-∞, {rule.DimensionValueMax}]";
            }

            return $" (valid range: value ∈ {interval})";
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
                }

                capturingLogger.LogDebug(
                    "Evaluating scale configuration {Id}: ScaleDownLockWindowMinutes={ScaleDownLockWindowMinutes}, ScaleUpAllowWindowMinutes={ScaleUpAllowWindowMinutes}",
                    setting.Id,
                    setting.ScaleDownLockWindowMinutes,
                    setting.ScaleUpAllowWindowMinutes);

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
                        if (state.LastScale != null && (utcNow - state.LastScale.Value).TotalSeconds < rule.ScaleDownCooldownSeconds)
                        {
                            capturingLogger.LogTrace(
                                "Skipping scale down from {Current} to {Target} because ScaleDownCooldownSeconds {Seconds}s have not yet passed.",
                                currentDimensionValue,
                                targetDimensionValue,
                                rule.ScaleDownCooldownSeconds);
                            continue;
                        }

                        if (setting.ScaleDownLockWindowMinutes.HasValue && utcNow.Minute >= setting.ScaleDownLockWindowMinutes)
                        {
                            capturingLogger.LogTrace(
                                "Skipping scale down from {Current} to {Target} not allowed from minute {Minute} onward (lock window) of a billable hour.",
                                currentDimensionValue,
                                targetDimensionValue,
                                setting.ScaleDownLockWindowMinutes);
                            continue;
                        }
                    }

                    if (dimension.Compare(state.Resource, targetDimensionValue, currentDimensionValue) == 1)
                    {
                        if (state.LastScale != null && (utcNow - state.LastScale.Value).TotalSeconds < rule.ScaleUpCooldownSeconds)
                        {
                            capturingLogger.LogTrace(
                                "Skipping scale up from {Current} to {Target} because ScaleUpCooldownSeconds {Seconds}s have not yet passed.",
                                currentDimensionValue,
                                targetDimensionValue,
                                rule.ScaleUpCooldownSeconds);
                            continue;
                        }

                        if (setting.ScaleDownLockWindowMinutes.HasValue && utcNow.Minute > setting.ScaleUpAllowWindowMinutes)
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

                    var rangeSuffix = FormatDimensionRangeSuffix(rule);
                    if (existingDimensionRequest != newDimensionRequest)
                    {
                        capturingLogger.LogDebug(
                            "Rule '{RuleId}' Dimension request changed from {From} to {To}{Range}",
                            rule.Id,
                            existingDimensionRequest ?? "(null)",
                            newDimensionRequest,
                            rangeSuffix);
                    }
                    else
                    {
                        capturingLogger.LogDebug(
                            "Rule '{RuleId}' requested target value '{Value}'{Range}",
                            rule.Id,
                            targetDimensionValue,
                            rangeSuffix);
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

                state.LastScale = this.utcNowProvider();
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
