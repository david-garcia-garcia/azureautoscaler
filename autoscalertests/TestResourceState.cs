using Azure.Core;
using Azure.ResourceManager;
using Microsoft.Extensions.Logging;
using poolautoscaler.configuration;
using poolautoscaler.metrics.Dto;
using poolautoscaler.resourcemanagement;
using poolautoscaler.resourcemanagement.Dto;

namespace poolautoscaler.tests
{
    /// <summary>
    /// Minimal <see cref="ResourceState"/> implementation for testing ResourceProcessor.
    /// Overrides Refresh to avoid Azure calls and optionally track invocations.
    /// </summary>
    internal sealed class TestResourceState : ResourceState
    {
        public TestResourceState(string id, ILogger logger, Resource configuration)
            : base(id, logger, configuration)
        {
        }

        public override object ExistingStateRaw => new { Capacity = this.CurrentCapacity };

        public override object RequestedStateRaw => new { Capacity = this.RequestedCapacity ?? this.CurrentCapacity };

        public bool RefreshWasCalled { get; private set; }

        public int CurrentCapacity { get; set; } = 10;

        public int? RequestedCapacity { get; set; }

        /// <summary>
        /// Metric values keyed by metric name (e.g. "custom_test_cpu"). Used by CustomMetric to return configurable data.
        /// </summary>
        public Dictionary<string, double> CustomMetricValues { get; set; } = new Dictionary<string, double>();

        /// <summary>When set, <see cref="ApplyChanges"/> throws this (for background scale task error tests).</summary>
        public Exception? ApplyChangesException { get; set; }

        public override Task<MetricEvalDtoResult> CustomMetric(
            ArmClient client,
            TokenCredential credential,
            CancellationToken cancellationToken,
            ScalingConfiguration setting,
            string name)
        {
            var value = this.CustomMetricValues.TryGetValue(name, out var v) ? v : 0;
            var result = new MetricEvalDtoResult
            {
                Values = new List<MetricEvalDtoResultValue>
                {
                    new MetricEvalDtoResultValue
                    {
                        Average = value,
                        Default = value,
                        TimeStamp = DateTimeOffset.UtcNow,
                    },
                },
                ExecutedAggregations = new List<Azure.Monitor.Query.Models.MetricAggregationType> { Azure.Monitor.Query.Models.MetricAggregationType.Average },
            };
            return Task.FromResult(result);
        }

        public override async Task Refresh(IArmClientWrapper clientWrapper, TokenCredential credential, CancellationToken cancellationToken)
        {
            this.RefreshWasCalled = true;

            // Contract: Refresh always sets LastScale so RunLoop can use it. Base uses change history; test double uses MinValue when unset.
            this.LastScale = this.LastScale ?? DateTime.MinValue;
            await Task.CompletedTask;
        }

        public override ResourcePatchOperation PreparePatch()
        {
            var hasChanges = this.RequestedCapacity.HasValue && this.RequestedCapacity.Value != this.CurrentCapacity;
            return new ResourcePatchOperation
            {
                HasChanges = hasChanges,
                PatchData = new { Capacity = this.RequestedCapacity ?? this.CurrentCapacity },
            };
        }

        public override Task ApplyChanges(ResourcePatchOperation operation, CancellationToken cancellationToken)
        {
            if (this.ApplyChangesException != null)
            {
                throw this.ApplyChangesException;
            }

            return Task.CompletedTask;
        }

        protected override Task InternalRefreshAsync(ArmClient client, TokenCredential credential, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
