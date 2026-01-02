using Azure.Core;
using Azure.ResourceManager;
using Microsoft.Extensions.Logging;
using poolautoscaler.resources;

namespace poolautoscaler;

internal interface IDimension
{
    bool CanApplyDimension(ResourceState resource, ScalingRule rule, ILogger logger);

    void ValidateRuleConfiguration(ScalingRule rule);

    int Compare(ArmResource resource, string dimensionValue1, string dimensionValue2);

    string GetCurrentDimensionValue(ResourceState resource);
    string? GetRequestedDimensionValue(ResourceState resource);
    string GetNextDimensionValue(ResourceState resource, string value);
    string GetPreviousDimensionValue(ResourceState resource, string value);

    Task SetDimensionValue(
         CancellationToken stoppingToken,
         ResourceState resource,
         ILogger logger,
         TokenCredential credential,
         string value);
}

internal static class IDimensionStatics
{
    /// <summary>
    /// Checks if the current value is greater than or equal to the target value
    /// </summary>
    internal static bool GreaterThanOrEqual(this IDimension dimension, string current, string target, ArmResource armResource)
    {
        return dimension.Compare(armResource, current, target) >= 0;
    }

    /// <summary>
    /// Checks if the current value is less than or equal to the target value
    /// </summary>
    internal static bool SmallerThanOrEqual(this IDimension dimension, string current, string target, ArmResource armResource)
    {
        return dimension.Compare(armResource, current, target) <= 0;
    }

    /// <summary>
    /// Checks if the current value is greater than or equal to the target value
    /// </summary>
    internal static bool GreaterThan(this IDimension dimension, string current, string target, ArmResource armResource)
    {
        return dimension.Compare(armResource, current, target) > 0;
    }

    /// <summary>
    /// Checks if the current value is less than or equal to the target value
    /// </summary>
    internal static bool SmallerThan(this IDimension dimension, string current, string target, ArmResource armResource)
    {
        return dimension.Compare(armResource, current, target) < 0;
    }
}