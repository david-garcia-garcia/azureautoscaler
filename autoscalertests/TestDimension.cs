using Azure;
using Azure.Core;
using Azure.ResourceManager;
using Microsoft.Extensions.Logging;
using poolautoscaler.configuration;
using poolautoscaler.dimensions;
using poolautoscaler.resourcemanagement;

namespace poolautoscaler.tests
{
    /// <summary>
    /// Test dimension for <see cref="TestResourceState"/>. Applies when rule.Dimension == "TestCapacity".
    /// Uses simple numeric comparison for capacity values.
    /// </summary>
    internal sealed class TestDimension : IDimension
    {
        /// <summary>When set, <see cref="SetDimensionValue"/> throws this instead of updating capacity (for transient Azure error tests).</summary>
        public RequestFailedException? ExceptionToThrowOnSet { get; set; }

        /// <inheritdoc/>
        public bool CanApplyDimension(ResourceState resource, ScalingRule rule, ILogger logger)
        {
            return resource is TestResourceState && rule.Dimension == "TestCapacity";
        }

        /// <inheritdoc/>
        public void ValidateRuleConfiguration(ScalingRule rule)
        {
            // No validation for tests
        }

        /// <inheritdoc/>
        public int Compare(ArmResource resource, string dimensionValue1, string dimensionValue2)
        {
            if (int.TryParse(dimensionValue1, out var value1) && int.TryParse(dimensionValue2, out var value2))
            {
                return value1.CompareTo(value2);
            }

            return string.Compare(dimensionValue1, dimensionValue2, StringComparison.Ordinal);
        }

        /// <inheritdoc/>
        public string GetCurrentDimensionValue(ResourceState resource)
        {
            return ((TestResourceState)resource).CurrentCapacity.ToString();
        }

        /// <inheritdoc/>
        public string? GetRequestedDimensionValue(ResourceState resource)
        {
            var requested = ((TestResourceState)resource).RequestedCapacity;
            return requested.HasValue ? requested.Value.ToString() : null;
        }

        /// <inheritdoc/>
        public string GetNextDimensionValue(ResourceState resource, string value)
        {
            var current = int.Parse(value);
            return (current + 1).ToString();
        }

        /// <inheritdoc/>
        public string GetPreviousDimensionValue(ResourceState resource, string value)
        {
            var current = int.Parse(value);
            return Math.Max(0, current - 1).ToString();
        }

        /// <inheritdoc/>
        public Task SetDimensionValue(
            CancellationToken stoppingToken,
            ResourceState resource,
            ILogger logger,
            TokenCredential credential,
            string value)
        {
            if (this.ExceptionToThrowOnSet != null)
            {
                throw this.ExceptionToThrowOnSet;
            }

            ((TestResourceState)resource).RequestedCapacity = int.Parse(value);
            return Task.CompletedTask;
        }
    }
}
