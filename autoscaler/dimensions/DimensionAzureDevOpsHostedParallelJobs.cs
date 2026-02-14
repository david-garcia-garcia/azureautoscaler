using Azure.Core;
using Azure.ResourceManager;
using Microsoft.Extensions.Logging;
using poolautoscaler.configuration;
using poolautoscaler.resourcemanagement;
using poolautoscaler.resources.AzureDevops;

namespace poolautoscaler.dimensions
{
    /// <summary>
    /// Dimension for scaling Azure DevOps Microsoft-hosted parallel jobs.
    /// This allows autoscaling the number of purchased parallel jobs based on pipeline queue metrics.
    /// </summary>
    internal class DimensionAzureDevOpsHostedParallelJobs : IDimension
    {
        public bool CanApplyDimension(ResourceState resource, ScalingRule rule, ILogger logger)
        {
            return resource is AzureDevOpsParallelJobsResourceState
                && rule.Dimension == "HostedParallelJobs";
        }

        public void ValidateRuleConfiguration(ScalingRule rule)
        {
            // Validate min/max if specified
            if (!string.IsNullOrEmpty(rule.DimensionValueMin))
            {
                if (!int.TryParse(rule.DimensionValueMin, out var min) || min < 0)
                {
                    throw new ArgumentException($"DimensionValueMin must be a non-negative integer for HostedParallelJobs dimension. Got: {rule.DimensionValueMin}");
                }
            }

            if (!string.IsNullOrEmpty(rule.DimensionValueMax))
            {
                if (!int.TryParse(rule.DimensionValueMax, out var max) || max < 0)
                {
                    throw new ArgumentException($"DimensionValueMax must be a non-negative integer for HostedParallelJobs dimension. Got: {rule.DimensionValueMax}");
                }
            }
        }

        public int Compare(ArmResource resource, string dimensionValue1, string dimensionValue2)
        {
            if (int.TryParse(dimensionValue1, out var value1) && int.TryParse(dimensionValue2, out var value2))
            {
                return value1.CompareTo(value2);
            }

            throw new ArgumentException($"Invalid dimension values for comparison. Value1: '{dimensionValue1}', Value2: '{dimensionValue2}'");
        }

        public string GetCurrentDimensionValue(ResourceState resource)
        {
            if (resource is not AzureDevOpsParallelJobsResourceState state)
            {
                throw new ArgumentException($"Resource is not {nameof(AzureDevOpsParallelJobsResourceState)}.");
            }

            return (state.ExistingParallelJobsState.HostedParallelJobs ?? 0).ToString();
        }

        public string? GetRequestedDimensionValue(ResourceState resource)
        {
            if (resource is not AzureDevOpsParallelJobsResourceState state)
            {
                throw new ArgumentException($"Resource is not {nameof(AzureDevOpsParallelJobsResourceState)}.");
            }

            return state.RequestedParallelJobsState.HostedParallelJobs?.ToString();
        }

        public string GetNextDimensionValue(ResourceState resource, string value)
        {
            return (int.Parse(value) + 1).ToString();
        }

        public string GetPreviousDimensionValue(ResourceState resource, string value)
        {
            var current = int.Parse(value);
            return Math.Max(0, current - 1).ToString();
        }

        public async Task SetDimensionValue(
            CancellationToken stoppingToken,
            ResourceState resource,
            ILogger logger,
            TokenCredential credential,
            string value)
        {
            if (resource is not AzureDevOpsParallelJobsResourceState state)
            {
                throw new ArgumentException($"Resource is not {nameof(AzureDevOpsParallelJobsResourceState)}.");
            }

            if (!int.TryParse(value, out var count) || count < 0)
            {
                throw new ArgumentException($"Invalid value for HostedParallelJobs: {value}. Must be a non-negative integer.");
            }

            state.SetHostedParallelJobs(count);

            await Task.CompletedTask;
        }
    }
}
