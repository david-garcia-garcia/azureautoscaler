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

        public override object ExistingStateRaw { get; } = new object();

        public override object RequestedStateRaw { get; } = new object();

        public bool RefreshWasCalled { get; private set; }

        public override Task<MetricEvalDtoResult> CustomMetric(
            ArmClient client,
            TokenCredential credential,
            CancellationToken cancellationToken,
            ScalingConfiguration setting,
            string name)
        {
            return Task.FromResult(new MetricEvalDtoResult { Values = new List<MetricEvalDtoResultValue>() });
        }

        public override async Task Refresh(ArmClient client, TokenCredential credential, CancellationToken cancellationToken)
        {
            this.RefreshWasCalled = true;
            await Task.CompletedTask;
        }

        public override ResourcePatchOperation PreparePatch()
        {
            return new ResourcePatchOperation { HasChanges = false };
        }

        public override Task ApplyChanges(ResourcePatchOperation operation, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        protected override Task InternalRefreshAsync(ArmClient client, TokenCredential credential, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        protected override string GetResourceIdForChangeHistory()
        {
            return string.Empty;
        }
    }
}
