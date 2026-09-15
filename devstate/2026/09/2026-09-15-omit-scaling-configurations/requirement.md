# Requirement
IssueKey: 2026-09-15-omit-scaling-configurations

## Problem
Resources whose YAML defines CustomMetrics but omits `ScalingConfigurations` crash every evaluation cycle with NullReferenceException. Unhandled-exception disable is set for one hour but never honored on subsequent cycles, so logs flood and refresh/scaling logic keeps failing.

## Current (code)
- `autoscaler/configuration/Configuration.cs` — `PrepareAndValidate` skips validation when `resource.ScalingConfigurations == null` (valid bind).
- `autoscaler/configuration/Resource.cs` — `ScalingConfigurations` is a dictionary property; omitted YAML key deserializes as null.
- `autoscaler/resourcemanagement/ResourceProcessor.cs` — `RunLoop` line ~260 dereferences `state.Configuration.ScalingConfigurations.Values` before null-safe handling.
- `autoscaler/resourcemanagement/ResourceProcessor.cs` — `RunLoop` calls `IsDisabled()` only after refresh/push and after the scaling-config query (~276), not before the null dereference.
- `autoscaler/resourcemanagement/ResourceProcessor.cs` — `ProcessOneAsync` has no early `IsDisabled()` guard before `RunLoop`; catch sets `DisabledUntil` for unhandled exceptions; `finally` always `ResetEvaluation()` so next wait is `Frequency` only.
- `autoscaler/resourcemanagement/ResourceState.cs` — `IsDisabled()` prunes expired keys and returns true when any disable entry is still active.
- `autoscalertests/ResourceProcessorTests.cs` — `ProcessOneAsync_WhenNoScalingConfigurations_StillRefreshesAndExitsBeforeScaling` uses `CreateResourceConfiguration(Array.Empty<...>())` (empty dictionary), not null.

## Desired
- Treat null `ScalingConfigurations` like empty: refresh and custom-metrics push run; exit before scaling evaluation; no throw.
- Log Information once per hour per resource (same throttle pattern as disabled-resource INFO in `RunLoop`) that the resource has no ScalingConfigurations configured, using the resource identity already used in logging.
- Keep Trace for time-window “no scaling configurations apply right now” when dictionary exists but none active.
- In `ProcessOneAsync`, honor `IsDisabled()` before entering `RunLoop`, reusing the existing once-per-hour disabled INFO in `RunLoop` (no duplicate spam). Keep post-refresh `IsDisabled()` in `RunLoop` for disables discovered during refresh.
- Tests: null `ScalingConfigurations` (YAML-omit shape) — no throw, refresh runs, INFO asserted if the suite supports log capture; after unhandled-exception disable, a later `ProcessOneAsync` within the hour must not re-run failing work or repeat fail+1-hour warn. Reuse `ResourceProcessorTests` helpers; synthetic ids only.

## Affected
- `autoscaler/resourcemanagement/ResourceProcessor.cs`
- `autoscaler/resourcemanagement/ResourceState.cs` (possible throttle timestamp field for no-config INFO)
- `autoscalertests/ResourceProcessorTests.cs`

## Out of scope
- Changing validation to require `ScalingConfigurations` in YAML.
- Catching NRE only without fixing disable ordering.
- Production resource names, subscription IDs, or live deploy YAML in tests.

## Unknowns
- Whether `ResourceState` already has a suitable last-logged timestamp for the no-config INFO or needs a new field (pattern exists: `LastDisabledMessageLogged` in `ResourceState.cs`).

## Tensions
None.
