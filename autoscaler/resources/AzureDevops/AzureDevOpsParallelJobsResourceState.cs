using Azure.Core;
using Azure.ResourceManager;
using Microsoft.Extensions.Logging;
using poolautoscaler.configuration;
using poolautoscaler.metrics;
using poolautoscaler.metrics.Dto;
using poolautoscaler.resourcemanagement;
using poolautoscaler.resourcemanagement.Dto;
using poolautoscaler.resources.AzureDevops.Dto;

namespace poolautoscaler.resources.AzureDevops
{
    /// <summary>
    /// Resource state for Azure DevOps parallel jobs (both MS-hosted and self-hosted).
    /// This uses the undocumented Azure DevOps Commerce API to manage parallel job counts.
    /// </summary>
    public class AzureDevOpsParallelJobsResourceState : ResourceState
    {
        /// <inheritdoc />
        public override object ExistingStateRaw => this.ExistingParallelJobsState;

        /// <inheritdoc />
        public override object RequestedStateRaw => this.RequestedParallelJobsState;

        /// <summary>
        /// Gets or sets the requested parallel jobs state (hosted and private counts).
        /// </summary>
        public ParallelJobsState RequestedParallelJobsState { get; set; } = new ParallelJobsState();

        /// <summary>
        /// Gets or sets the current existing parallel jobs state from Azure DevOps.
        /// </summary>
        public ParallelJobsState ExistingParallelJobsState { get; set; } = new ParallelJobsState();

        /// <summary>
        /// Gets the Azure DevOps organization name (from resource ID).
        /// </summary>
        public string Organization { get; private set; }

        /// <summary>
        /// Gets the personal access token used for API calls.
        /// </summary>
        public string PersonalAccessToken { get; private set; }

        private string? organizationId;
        private string? billingToken;
        private DateTime billingTokenExpiry = DateTime.MinValue;

        private readonly AzureDevOpsClient devOpsClient;

        /// <summary>
        /// Initializes a new instance of the <see cref="AzureDevOpsParallelJobsResourceState"/> class.
        /// </summary>
        /// <param name="resourceId">The resource ID (azuredevops://organization).</param>
        /// <param name="logger">The logger.</param>
        /// <param name="resourceConfiguration">The resource configuration.</param>
        /// <param name="resourceInstance">Optional resource instance (must contain Pat in Settings).</param>
        /// <param name="resourceLocationResolver">Resource location resolver (not used for Azure DevOps but kept for consistency).</param>
        /// <param name="vmSizeResolver">VM size resolver (not used for Azure DevOps but injected for consistency across resource states).</param>
        public AzureDevOpsParallelJobsResourceState(
            string resourceId,
            ILogger logger,
            Resource resourceConfiguration,
            IResourceLocationResolver resourceLocationResolver,
            IVmSizeResolver vmSizeResolver,
            ResourceInstance? resourceInstance = null)
            : base(resourceId, logger, resourceConfiguration, resourceLocationResolver, vmSizeResolver)
        {
            // Resource ID format: azuredevops://{organization}
            if (!ResourceStateFactory.AzureDevOpsParallelJobs.IsMatch(resourceId))
            {
                throw new ArgumentException("Invalid Azure DevOps Parallel Jobs resource ID. Expected format: azuredevops://{organization}", nameof(resourceId));
            }

            var match = ResourceStateFactory.AzureDevOpsParallelJobs.Match(resourceId);
            this.Organization = match.Groups["organization"].Value;

            // Get PAT from Settings (required)
            // Value can be direct PAT or "env://VAR_NAME" to read from environment variable
            var patSetting = resourceInstance?.Settings?.GetValueOrDefault("Pat");
            if (string.IsNullOrEmpty(patSetting))
            {
                throw new ArgumentException("Azure DevOps PAT not configured. Add 'Pat' to Settings in the resource configuration.");
            }

            this.PersonalAccessToken = ResolveSettingValue(patSetting, "Pat");

            this.devOpsClient = new AzureDevOpsClient(logger);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="AzureDevOpsParallelJobsResourceState"/> class (for testing with injected client).
        /// </summary>
        /// <param name="resourceId">The resource ID.</param>
        /// <param name="logger">The logger.</param>
        /// <param name="resourceConfiguration">The resource configuration.</param>
        /// <param name="client">The Azure DevOps client to use.</param>
        /// <param name="testOrganizationId">Optional organization ID for tests.</param>
        internal AzureDevOpsParallelJobsResourceState(string resourceId, ILogger logger, Resource resourceConfiguration, AzureDevOpsClient client, string? testOrganizationId = null)
            : base(resourceId, logger, resourceConfiguration)
        {
            if (!ResourceStateFactory.AzureDevOpsParallelJobs.IsMatch(resourceId))
            {
                throw new ArgumentException("Invalid Azure DevOps Parallel Jobs resource ID. Expected format: azuredevops://{organization}", nameof(resourceId));
            }

            var match = ResourceStateFactory.AzureDevOpsParallelJobs.Match(resourceId);
            this.Organization = match.Groups["organization"].Value;
            this.organizationId = testOrganizationId ?? "test-org-id"; // For testing
            this.PersonalAccessToken = "test-pat"; // For testing
            this.devOpsClient = client;
        }

        /// <summary>
        /// Sets the requested hosted (MS-hosted) parallel jobs count.
        /// </summary>
        /// <param name="count">The requested count (must be non-negative).</param>
        public void SetHostedParallelJobs(int count)
        {
            if (count < 0)
            {
                throw new ArgumentException("Parallel jobs count cannot be negative", nameof(count));
            }

            // Take the higher value if multiple rules request different values
            if (this.RequestedParallelJobsState.HostedParallelJobs == null ||
                count > this.RequestedParallelJobsState.HostedParallelJobs)
            {
                this.RequestedParallelJobsState.HostedParallelJobs = count;
            }
        }

        /// <summary>
        /// Sets the requested private (self-hosted) parallel jobs count.
        /// </summary>
        /// <param name="count">The requested count (must be non-negative).</param>
        public void SetPrivateParallelJobs(int count)
        {
            if (count < 0)
            {
                throw new ArgumentException("Parallel jobs count cannot be negative", nameof(count));
            }

            // Take the higher value if multiple rules request different values
            if (this.RequestedParallelJobsState.PrivateParallelJobs == null ||
                count > this.RequestedParallelJobsState.PrivateParallelJobs)
            {
                this.RequestedParallelJobsState.PrivateParallelJobs = count;
            }
        }

        /// <inheritdoc />
        public override ResourcePatchOperation PreparePatch()
        {
            var result = new ResourcePatchOperation();
            var patch = new ParallelJobsState();

            patch.HostedParallelJobs = this.RequestedParallelJobsState.HostedParallelJobs ?? this.ExistingParallelJobsState.HostedParallelJobs;
            patch.PrivateParallelJobs = this.RequestedParallelJobsState.PrivateParallelJobs ?? this.ExistingParallelJobsState.PrivateParallelJobs;

            bool hasHostedChanges = patch.HostedParallelJobs != this.ExistingParallelJobsState.HostedParallelJobs;
            bool hasPrivateChanges = patch.PrivateParallelJobs != this.ExistingParallelJobsState.PrivateParallelJobs;

            result.PatchData = patch;
            result.HasChanges = hasHostedChanges || hasPrivateChanges;
            result.Disruptive = false; // Changing parallel jobs is not disruptive

            return result;
        }

        /// <inheritdoc />
        public override async Task ApplyChanges(ResourcePatchOperation operation, CancellationToken cancellationToken)
        {
            if (!(operation.PatchData is ParallelJobsState patch))
            {
                throw new Exception("Invalid patch type.");
            }

            // Ensure organization ID is resolved (should already be from refresh, but just in case)
            await this.EnsureOrganizationIdAsync(cancellationToken);
            await this.EnsureBillingTokenAsync(cancellationToken);

            // Apply hosted parallel jobs changes
            if (patch.HostedParallelJobs.HasValue && patch.HostedParallelJobs != this.ExistingParallelJobsState.HostedParallelJobs)
            {
                this.Logger.LogInformation(
                    "Changing hosted parallel jobs from {From} to {To}",
                    this.ExistingParallelJobsState.HostedParallelJobs,
                    patch.HostedParallelJobs);

                await this.devOpsClient.SetParallelJobsAsync(
                    this.organizationId!,
                    this.billingToken!,
                    AzureDevOpsClient.MeterIdHostedPipeline,
                    patch.HostedParallelJobs.Value,
                    cancellationToken);
            }

            // Apply private parallel jobs changes
            if (patch.PrivateParallelJobs.HasValue && patch.PrivateParallelJobs != this.ExistingParallelJobsState.PrivateParallelJobs)
            {
                this.Logger.LogInformation(
                    "Changing private parallel jobs from {From} to {To}",
                    this.ExistingParallelJobsState.PrivateParallelJobs,
                    patch.PrivateParallelJobs);

                await this.devOpsClient.SetParallelJobsAsync(
                    this.organizationId!,
                    this.billingToken!,
                    AzureDevOpsClient.MeterIdPrivatePipeline,
                    patch.PrivateParallelJobs.Value,
                    cancellationToken);
            }
        }

        /// <summary>
        /// Custom metric implementation to get queued/running/free jobs count from Azure DevOps.
        ///
        /// Supported metrics:
        /// - custom_azdo_queued_hosted: Total queued jobs across all MS-hosted pools.
        /// - custom_azdo_queued_self_hosted: Total queued jobs across all self-hosted pools.
        /// - custom_azdo_running_hosted: Total running jobs across all MS-hosted pools.
        /// - custom_azdo_running_self_hosted: Total running jobs across all self-hosted pools.
        /// - custom_azdo_available_hosted: Available hosted capacity (parallel jobs limit - running jobs).
        /// - custom_azdo_available_self_hosted: Available self-hosted capacity (parallel jobs limit - running jobs).
        /// </summary>
        /// <param name="client">The ARM client.</param>
        /// <param name="credential">The token credential.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <param name="setting">The scaling configuration.</param>
        /// <param name="name">The custom metric name.</param>
        /// <returns>Metric result with the custom metric value.</returns>
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
                        var jobsInfo = await this.devOpsClient.GetAggregatedQueuedJobsAsync(this.Organization, this.PersonalAccessToken, hostedOnly: true, cancellationToken);
                        metricValue = jobsInfo.QueuedJobs;
                        this.Logger.LogTrace(
                            "Custom metric {MetricName}: {Value} (Queued={Queued}, Running={Running})",
                            name,
                            metricValue,
                            jobsInfo.QueuedJobs,
                            jobsInfo.RunningJobs);
                        break;
                    }

                case "custom_azdo_queued_self_hosted":
                    {
                        var jobsInfo = await this.devOpsClient.GetAggregatedQueuedJobsAsync(this.Organization, this.PersonalAccessToken, hostedOnly: false, cancellationToken);
                        metricValue = jobsInfo.QueuedJobs;
                        this.Logger.LogTrace(
                            "Custom metric {MetricName}: {Value} (Queued={Queued}, Running={Running})",
                            name,
                            metricValue,
                            jobsInfo.QueuedJobs,
                            jobsInfo.RunningJobs);
                        break;
                    }

                case "custom_azdo_running_hosted":
                    {
                        var jobsInfo = await this.devOpsClient.GetAggregatedQueuedJobsAsync(this.Organization, this.PersonalAccessToken, hostedOnly: true, cancellationToken);
                        metricValue = jobsInfo.RunningJobs;
                        this.Logger.LogTrace(
                            "Custom metric {MetricName}: {Value} (Queued={Queued}, Running={Running})",
                            name,
                            metricValue,
                            jobsInfo.QueuedJobs,
                            jobsInfo.RunningJobs);
                        break;
                    }

                case "custom_azdo_running_self_hosted":
                    {
                        var jobsInfo = await this.devOpsClient.GetAggregatedQueuedJobsAsync(this.Organization, this.PersonalAccessToken, hostedOnly: false, cancellationToken);
                        metricValue = jobsInfo.RunningJobs;
                        this.Logger.LogTrace(
                            "Custom metric {MetricName}: {Value} (Queued={Queued}, Running={Running})",
                            name,
                            metricValue,
                            jobsInfo.QueuedJobs,
                            jobsInfo.RunningJobs);
                        break;
                    }

                case "custom_azdo_available_hosted":
                    {
                        var jobsInfo = await this.devOpsClient.GetAggregatedQueuedJobsAsync(this.Organization, this.PersonalAccessToken, hostedOnly: true, cancellationToken);
                        var limit = this.ExistingParallelJobsState.HostedParallelJobs ?? 0;
                        metricValue = Math.Max(0, limit - jobsInfo.RunningJobs);
                        this.Logger.LogTrace(
                            "Custom metric {MetricName}: {Value} (Limit={Limit}, Running={Running})",
                            name,
                            metricValue,
                            limit,
                            jobsInfo.RunningJobs);
                        break;
                    }

                case "custom_azdo_available_self_hosted":
                    {
                        var jobsInfo = await this.devOpsClient.GetAggregatedQueuedJobsAsync(this.Organization, this.PersonalAccessToken, hostedOnly: false, cancellationToken);
                        var limit = this.ExistingParallelJobsState.PrivateParallelJobs ?? 0;
                        metricValue = Math.Max(0, limit - jobsInfo.RunningJobs);
                        this.Logger.LogTrace(
                            "Custom metric {MetricName}: {Value} (Limit={Limit}, Running={Running})",
                            name,
                            metricValue,
                            limit,
                            jobsInfo.RunningJobs);
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

        /// <inheritdoc />
        protected override string? GetResourceIdForChangeHistory()
        {
            return null;
        }

        /// <inheritdoc />
        protected override async Task InternalRefreshAsync(ArmClient client, TokenCredential credential, CancellationToken cancellationToken)
        {
            this.Logger.LogDebug("Refreshing Azure DevOps parallel jobs state for organization {Organization}", this.Organization);
            await this.EnsureOrganizationIdAsync(cancellationToken);
            await this.EnsureBillingTokenAsync(cancellationToken);
            var parallelJobs = await this.devOpsClient.GetParallelJobsAsync(this.organizationId!, this.billingToken!, cancellationToken);
            this.ExistingParallelJobsState = new ParallelJobsState
            {
                HostedParallelJobs = parallelJobs.HostedParallelJobs,
                PrivateParallelJobs = parallelJobs.PrivateParallelJobs
            };
            this.RequestedParallelJobsState = new ParallelJobsState();
            this.Logger.LogDebug(
                "Current parallel jobs - Hosted: {Hosted}, Private: {Private}",
                this.ExistingParallelJobsState.HostedParallelJobs,
                this.ExistingParallelJobsState.PrivateParallelJobs);
            this.Resource = null;
            this.ResourceTags["organization"] = this.Organization;
            this.ResourceTags["organizationId"] = this.organizationId!;
        }

        private async Task EnsureOrganizationIdAsync(CancellationToken cancellationToken)
        {
            if (this.organizationId == null)
            {
                this.Logger.LogDebug("Resolving organization ID for '{Organization}'", this.Organization);
                this.organizationId = await this.devOpsClient.GetOrganizationIdAsync(this.Organization, this.PersonalAccessToken, cancellationToken);
            }
        }

        private static string ResolveSettingValue(string value, string settingName)
        {
            if (value.StartsWith("env://", StringComparison.OrdinalIgnoreCase))
            {
                var envVarName = value.Substring(6);
                var envValue = Environment.GetEnvironmentVariable(envVarName);
                if (string.IsNullOrEmpty(envValue))
                {
                    throw new ArgumentException($"Environment variable '{envVarName}' specified for setting '{settingName}' is not set or is empty.");
                }

                return envValue;
            }

            return value;
        }

        private async Task EnsureBillingTokenAsync(CancellationToken cancellationToken)
        {
            if (this.billingToken == null || DateTime.UtcNow >= this.billingTokenExpiry)
            {
                this.Logger.LogDebug("Obtaining new billing token for organization {Organization}", this.Organization);
                this.billingToken = await this.devOpsClient.GetBillingTokenAsync(this.Organization, this.PersonalAccessToken, cancellationToken);
                this.billingTokenExpiry = DateTime.UtcNow.AddMinutes(50);
            }
        }
    }
}
