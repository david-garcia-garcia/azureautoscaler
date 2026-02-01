using Azure.Core;
using Azure.ResourceManager;
using Microsoft.Extensions.Logging;
using poolautoscaler.strategies;

namespace poolautoscaler.resources
{
    /// <summary>
    /// Resource state for Azure DevOps parallel jobs (both MS-hosted and self-hosted).
    /// This uses the undocumented Azure DevOps Commerce API to manage parallel job counts.
    /// </summary>
    public class AzureDevOpsParallelJobsResourceState : ResourceState
    {
        public class ParallelJobsState
        {
            public int? HostedParallelJobs { get; set; }
            public int? PrivateParallelJobs { get; set; }
        }

        public override object ExistingStateRaw => this.ExistingParallelJobsState;
        public override object RequestedStateRaw => this.RequestedParallelJobsState;

        public ParallelJobsState RequestedParallelJobsState { get; set; } = new ParallelJobsState();
        public ParallelJobsState ExistingParallelJobsState { get; set; } = new ParallelJobsState();

        // Azure DevOps configuration (extracted from resource ID)
        public string Organization { get; private set; }
        public string PersonalAccessToken { get; private set; }
        
        // Organization ID is resolved on first API call
        private string? _organizationId;

        // Cache the billing token
        private string? _billingToken;
        private DateTime _billingTokenExpiry = DateTime.MinValue;

        private readonly AzureDevOpsClient _devOpsClient;

        public AzureDevOpsParallelJobsResourceState(string resourceId, ILogger logger, Resource resourceConfiguration, ResourceInstance? resourceInstance = null)
            : base(resourceId, logger, resourceConfiguration)
        {
            // Resource ID format: azuredevops://{organization}
            if (!ResourceStateFactory.AzureDevOpsParallelJobs.IsMatch(resourceId))
            {
                throw new ArgumentException("Invalid Azure DevOps Parallel Jobs resource ID. Expected format: azuredevops://{organization}", nameof(resourceId));
            }

            var match = ResourceStateFactory.AzureDevOpsParallelJobs.Match(resourceId);
            Organization = match.Groups["organization"].Value;

            // Get PAT from Settings (required)
            // Value can be direct PAT or "env://VAR_NAME" to read from environment variable
            var patSetting = resourceInstance?.Settings?.GetValueOrDefault("Pat");
            if (string.IsNullOrEmpty(patSetting))
            {
                throw new ArgumentException("Azure DevOps PAT not configured. Add 'Pat' to Settings in the resource configuration.");
            }
            PersonalAccessToken = ResolveSettingValue(patSetting, "Pat");

            _devOpsClient = new AzureDevOpsClient(logger);
        }

        // Constructor for testing with injected client
        internal AzureDevOpsParallelJobsResourceState(string resourceId, ILogger logger, Resource resourceConfiguration, AzureDevOpsClient client, string? testOrganizationId = null)
            : base(resourceId, logger, resourceConfiguration)
        {
            if (!ResourceStateFactory.AzureDevOpsParallelJobs.IsMatch(resourceId))
            {
                throw new ArgumentException("Invalid Azure DevOps Parallel Jobs resource ID. Expected format: azuredevops://{organization}", nameof(resourceId));
            }

            var match = ResourceStateFactory.AzureDevOpsParallelJobs.Match(resourceId);
            Organization = match.Groups["organization"].Value;
            _organizationId = testOrganizationId ?? "test-org-id"; // For testing
            PersonalAccessToken = "test-pat"; // For testing
            _devOpsClient = client;
        }

        /// <summary>
        /// Ensures the organization ID has been resolved from the organization name.
        /// </summary>
        private async Task EnsureOrganizationIdAsync(CancellationToken cancellationToken)
        {
            if (_organizationId == null)
            {
                Logger.LogDebug("Resolving organization ID for '{Organization}'", Organization);
                _organizationId = await _devOpsClient.GetOrganizationIdAsync(Organization, PersonalAccessToken, cancellationToken);
            }
        }

        /// <summary>
        /// Resolves a setting value. If the value starts with "env://", reads from the specified environment variable.
        /// Otherwise, returns the value directly.
        /// </summary>
        private static string ResolveSettingValue(string value, string settingName)
        {
            if (value.StartsWith("env://", StringComparison.OrdinalIgnoreCase))
            {
                var envVarName = value.Substring(6); // "env://".Length = 6
                var envValue = Environment.GetEnvironmentVariable(envVarName);
                if (string.IsNullOrEmpty(envValue))
                {
                    throw new ArgumentException($"Environment variable '{envVarName}' specified for setting '{settingName}' is not set or is empty.");
                }
                return envValue;
            }
            return value;
        }

        protected override async Task InternalRefreshAsync(ArmClient client, TokenCredential credential, CancellationToken cancellationToken)
        {
            // Note: We don't use ARM client here since Azure DevOps is not an ARM resource
            // Instead we use the Azure DevOps REST APIs

            Logger.LogDebug("Refreshing Azure DevOps parallel jobs state for organization {Organization}", Organization);

            // Ensure organization ID is resolved
            await EnsureOrganizationIdAsync(cancellationToken);

            // Ensure we have a valid billing token
            await EnsureBillingTokenAsync(cancellationToken);

            // Get current parallel jobs count
            var parallelJobs = await _devOpsClient.GetParallelJobsAsync(_organizationId!, _billingToken!, cancellationToken);

            ExistingParallelJobsState = new ParallelJobsState
            {
                HostedParallelJobs = parallelJobs.HostedParallelJobs,
                PrivateParallelJobs = parallelJobs.PrivateParallelJobs
            };

            RequestedParallelJobsState = new ParallelJobsState();

            Logger.LogInformation("Current parallel jobs - Hosted: {Hosted}, Private: {Private}",
                ExistingParallelJobsState.HostedParallelJobs,
                ExistingParallelJobsState.PrivateParallelJobs);

            // No ARM resource, so set to null
            this.Resource = null;

            // Populate "tags" with organization info for filtering
            this.ResourceTags["organization"] = Organization;
            this.ResourceTags["organizationId"] = _organizationId!;
        }

        private async Task EnsureBillingTokenAsync(CancellationToken cancellationToken)
        {
            // Token expires, refresh if needed (assume 1 hour validity, refresh at 50 minutes)
            if (_billingToken == null || DateTime.UtcNow >= _billingTokenExpiry)
            {
                Logger.LogDebug("Obtaining new billing token for organization {Organization}", Organization);
                _billingToken = await _devOpsClient.GetBillingTokenAsync(Organization, PersonalAccessToken, cancellationToken);
                _billingTokenExpiry = DateTime.UtcNow.AddMinutes(50);
            }
        }

        protected override string GetResourceIdForChangeHistory()
        {
            // Azure DevOps doesn't have ARM change history
            return null;
        }

        public void SetHostedParallelJobs(int count)
        {
            if (count < 0)
            {
                throw new ArgumentException("Parallel jobs count cannot be negative", nameof(count));
            }

            // Take the higher value if multiple rules request different values
            if (RequestedParallelJobsState.HostedParallelJobs == null ||
                count > RequestedParallelJobsState.HostedParallelJobs)
            {
                RequestedParallelJobsState.HostedParallelJobs = count;
            }
        }

        public void SetPrivateParallelJobs(int count)
        {
            if (count < 0)
            {
                throw new ArgumentException("Parallel jobs count cannot be negative", nameof(count));
            }

            // Take the higher value if multiple rules request different values
            if (RequestedParallelJobsState.PrivateParallelJobs == null ||
                count > RequestedParallelJobsState.PrivateParallelJobs)
            {
                RequestedParallelJobsState.PrivateParallelJobs = count;
            }
        }

        public override ResourcePatchOperation PreparePatch()
        {
            var result = new ResourcePatchOperation();
            var patch = new ParallelJobsState();

            patch.HostedParallelJobs = RequestedParallelJobsState.HostedParallelJobs ?? ExistingParallelJobsState.HostedParallelJobs;
            patch.PrivateParallelJobs = RequestedParallelJobsState.PrivateParallelJobs ?? ExistingParallelJobsState.PrivateParallelJobs;

            bool hasHostedChanges = patch.HostedParallelJobs != ExistingParallelJobsState.HostedParallelJobs;
            bool hasPrivateChanges = patch.PrivateParallelJobs != ExistingParallelJobsState.PrivateParallelJobs;

            result.PatchData = patch;
            result.HasChanges = hasHostedChanges || hasPrivateChanges;
            result.Disruptive = false; // Changing parallel jobs is not disruptive

            return result;
        }

        public override async Task ApplyChanges(ResourcePatchOperation operation, CancellationToken cancellationToken)
        {
            if (!(operation.PatchData is ParallelJobsState patch))
            {
                throw new Exception("Invalid patch type.");
            }

            // Ensure organization ID is resolved (should already be from refresh, but just in case)
            await EnsureOrganizationIdAsync(cancellationToken);
            await EnsureBillingTokenAsync(cancellationToken);

            // Apply hosted parallel jobs changes
            if (patch.HostedParallelJobs.HasValue && patch.HostedParallelJobs != ExistingParallelJobsState.HostedParallelJobs)
            {
                Logger.LogInformation("Changing hosted parallel jobs from {From} to {To}",
                    ExistingParallelJobsState.HostedParallelJobs, patch.HostedParallelJobs);

                await _devOpsClient.SetParallelJobsAsync(
                    _organizationId!,
                    _billingToken!,
                    AzureDevOpsClient.MeterIdHostedPipeline,
                    patch.HostedParallelJobs.Value,
                    cancellationToken);
            }

            // Apply private parallel jobs changes
            if (patch.PrivateParallelJobs.HasValue && patch.PrivateParallelJobs != ExistingParallelJobsState.PrivateParallelJobs)
            {
                Logger.LogInformation("Changing private parallel jobs from {From} to {To}",
                    ExistingParallelJobsState.PrivateParallelJobs, patch.PrivateParallelJobs);

                await _devOpsClient.SetParallelJobsAsync(
                    _organizationId!,
                    _billingToken!,
                    AzureDevOpsClient.MeterIdPrivatePipeline,
                    patch.PrivateParallelJobs.Value,
                    cancellationToken);
            }
        }

        /// <summary>
        /// Custom metric implementation to get queued/running/free jobs count from Azure DevOps.
        /// 
        /// Supported metrics:
        /// - custom_azdo_queued_hosted: Total queued jobs across all MS-hosted pools
        /// - custom_azdo_queued_self_hosted: Total queued jobs across all self-hosted pools
        /// - custom_azdo_running_hosted: Total running jobs across all MS-hosted pools
        /// - custom_azdo_running_self_hosted: Total running jobs across all self-hosted pools
        /// - custom_azdo_available_hosted: Available hosted capacity (parallel jobs limit - running jobs)
        /// - custom_azdo_available_self_hosted: Available self-hosted capacity (parallel jobs limit - running jobs)
        /// </summary>
        public override async Task<MetricEvalDtoResult> CustomMetric(
            ArmClient client,
            TokenCredential credential,
            CancellationToken cancellationToken,
            ScalingConfiguration setting,
            string name)
        {
            double metricValue;

            switch (name)
            {
                case "custom_azdo_queued_hosted":
                {
                    var jobsInfo = await _devOpsClient.GetAggregatedQueuedJobsAsync(Organization, PersonalAccessToken, hostedOnly: true, cancellationToken);
                    metricValue = jobsInfo.QueuedJobs;
                    Logger.LogDebug("Custom metric {MetricName}: {Value} (Queued={Queued}, Running={Running})",
                        name, metricValue, jobsInfo.QueuedJobs, jobsInfo.RunningJobs);
                    break;
                }

                case "custom_azdo_queued_self_hosted":
                {
                    var jobsInfo = await _devOpsClient.GetAggregatedQueuedJobsAsync(Organization, PersonalAccessToken, hostedOnly: false, cancellationToken);
                    metricValue = jobsInfo.QueuedJobs;
                    Logger.LogDebug("Custom metric {MetricName}: {Value} (Queued={Queued}, Running={Running})",
                        name, metricValue, jobsInfo.QueuedJobs, jobsInfo.RunningJobs);
                    break;
                }

                case "custom_azdo_running_hosted":
                {
                    var jobsInfo = await _devOpsClient.GetAggregatedQueuedJobsAsync(Organization, PersonalAccessToken, hostedOnly: true, cancellationToken);
                    metricValue = jobsInfo.RunningJobs;
                    Logger.LogDebug("Custom metric {MetricName}: {Value} (Queued={Queued}, Running={Running})",
                        name, metricValue, jobsInfo.QueuedJobs, jobsInfo.RunningJobs);
                    break;
                }

                case "custom_azdo_running_self_hosted":
                {
                    var jobsInfo = await _devOpsClient.GetAggregatedQueuedJobsAsync(Organization, PersonalAccessToken, hostedOnly: false, cancellationToken);
                    metricValue = jobsInfo.RunningJobs;
                    Logger.LogDebug("Custom metric {MetricName}: {Value} (Queued={Queued}, Running={Running})",
                        name, metricValue, jobsInfo.QueuedJobs, jobsInfo.RunningJobs);
                    break;
                }

                case "custom_azdo_available_hosted":
                {
                    var jobsInfo = await _devOpsClient.GetAggregatedQueuedJobsAsync(Organization, PersonalAccessToken, hostedOnly: true, cancellationToken);
                    var limit = ExistingParallelJobsState.HostedParallelJobs ?? 0;
                    metricValue = Math.Max(0, limit - jobsInfo.RunningJobs);
                    Logger.LogDebug("Custom metric {MetricName}: {Value} (Limit={Limit}, Running={Running})",
                        name, metricValue, limit, jobsInfo.RunningJobs);
                    break;
                }

                case "custom_azdo_available_self_hosted":
                {
                    var jobsInfo = await _devOpsClient.GetAggregatedQueuedJobsAsync(Organization, PersonalAccessToken, hostedOnly: false, cancellationToken);
                    var limit = ExistingParallelJobsState.PrivateParallelJobs ?? 0;
                    metricValue = Math.Max(0, limit - jobsInfo.RunningJobs);
                    Logger.LogDebug("Custom metric {MetricName}: {Value} (Limit={Limit}, Running={Running})",
                        name, metricValue, limit, jobsInfo.RunningJobs);
                    break;
                }

                default:
                    throw new NotSupportedException($"Custom metric '{name}' is not supported for Azure DevOps resources. " +
                        "Supported metrics: custom_azdo_queued_hosted, custom_azdo_queued_self_hosted, " +
                        "custom_azdo_running_hosted, custom_azdo_running_self_hosted, " +
                        "custom_azdo_available_hosted, custom_azdo_available_self_hosted");
            }

            var result = new MetricEvalDtoResult
            {
                Values = new List<MetricEvalDtoResultValue>
                {
                    new MetricEvalDtoResultValue
                    {
                        Average = metricValue,
                        TimeStamp = DateTimeOffset.UtcNow
                    }
                }
            };

            return result;
        }
    }
}
