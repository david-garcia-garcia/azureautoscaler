using Azure.Core;
using Azure.ResourceManager;
using Microsoft.Extensions.Logging;
using poolautoscaler.configuration;
using poolautoscaler.resourcemanagement;

namespace poolautoscaler.dimensions
{
    /// <summary>Dimension that can be read/compared/set on a resource.</summary>
    internal interface IDimension
    {
        /// <summary>Returns true if this dimension applies to the resource and rule.</summary>
        /// <param name="resource">The resource state.</param>
        /// <param name="rule">The scaling rule.</param>
        /// <param name="logger">The logger.</param>
        /// <returns>True if this dimension applies.</returns>
        bool CanApplyDimension(ResourceState resource, ScalingRule rule, ILogger logger);

        /// <summary>Validates rule configuration for this dimension.</summary>
        /// <param name="rule">The scaling rule.</param>
        void ValidateRuleConfiguration(ScalingRule rule);

        /// <summary>Compares two dimension values (-1, 0, or 1).</summary>
        /// <param name="resource">The ARM resource.</param>
        /// <param name="dimensionValue1">First dimension value.</param>
        /// <param name="dimensionValue2">Second dimension value.</param>
        /// <returns>Comparison result: -1, 0, or 1.</returns>
        int Compare(ArmResource resource, string dimensionValue1, string dimensionValue2);

        /// <summary>Current dimension value on the resource.</summary>
        /// <param name="resource">The resource state.</param>
        /// <returns>Current value as string.</returns>
        string GetCurrentDimensionValue(ResourceState resource);

        /// <summary>Requested value if a change is pending.</summary>
        /// <param name="resource">The resource state.</param>
        /// <returns>Requested value or null.</returns>
        string? GetRequestedDimensionValue(ResourceState resource);

        /// <summary>Next value after stepping from the given value.</summary>
        /// <param name="resource">The resource state.</param>
        /// <param name="value">The current dimension value.</param>
        /// <returns>Next dimension value.</returns>
        string GetNextDimensionValue(ResourceState resource, string value);

        /// <summary>Previous value after stepping from the given value.</summary>
        /// <param name="resource">The resource state.</param>
        /// <param name="value">The current dimension value.</param>
        /// <returns>Previous dimension value.</returns>
        string GetPreviousDimensionValue(ResourceState resource, string value);

        /// <summary>Applies the dimension value to the resource.</summary>
        /// <param name="stoppingToken">Cancellation token.</param>
        /// <param name="resource">The resource state.</param>
        /// <param name="logger">The logger.</param>
        /// <param name="credential">The token credential.</param>
        /// <param name="value">The dimension value to set.</param>
        /// <returns>A task that completes when the value is set.</returns>
        Task SetDimensionValue(
             CancellationToken stoppingToken,
             ResourceState resource,
             ILogger logger,
             TokenCredential credential,
             string value);
    }

    /// <summary>Extension methods for dimension comparison.</summary>
    internal static class IDimensionStatics
    {
        /// <summary>
        /// Checks if the current value is greater than or equal to the target value.
        /// </summary>
        /// <param name="dimension">The dimension.</param>
        /// <param name="current">Current dimension value.</param>
        /// <param name="target">Target dimension value.</param>
        /// <param name="armResource">The ARM resource.</param>
        /// <returns>True if current is greater than or equal to target.</returns>
        internal static bool GreaterThanOrEqual(this IDimension dimension, string current, string target, ArmResource armResource)
        {
            return dimension.Compare(armResource, current, target) >= 0;
        }

        /// <summary>
        /// Checks if the current value is less than or equal to the target value.
        /// </summary>
        /// <param name="dimension">The dimension.</param>
        /// <param name="current">Current dimension value.</param>
        /// <param name="target">Target dimension value.</param>
        /// <param name="armResource">The ARM resource.</param>
        /// <returns>True if current is less than or equal to target.</returns>
        internal static bool SmallerThanOrEqual(this IDimension dimension, string current, string target, ArmResource armResource)
        {
            return dimension.Compare(armResource, current, target) <= 0;
        }

        /// <summary>
        /// Checks if the current value is greater than the target value.
        /// </summary>
        /// <param name="dimension">The dimension.</param>
        /// <param name="current">Current dimension value.</param>
        /// <param name="target">Target dimension value.</param>
        /// <param name="armResource">The ARM resource.</param>
        /// <returns>True if current is greater than target.</returns>
        internal static bool GreaterThan(this IDimension dimension, string current, string target, ArmResource armResource)
        {
            return dimension.Compare(armResource, current, target) > 0;
        }

        /// <summary>
        /// Checks if the current value is less than the target value.
        /// </summary>
        /// <param name="dimension">The dimension.</param>
        /// <param name="current">Current dimension value.</param>
        /// <param name="target">Target dimension value.</param>
        /// <param name="armResource">The ARM resource.</param>
        /// <returns>True if current is less than target.</returns>
        internal static bool SmallerThan(this IDimension dimension, string current, string target, ArmResource armResource)
        {
            return dimension.Compare(armResource, current, target) < 0;
        }
    }
}
