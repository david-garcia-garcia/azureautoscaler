# Resource evaluation

## Language

**Absent scaling configurations**:
The resource has no scaling dictionary after bind (YAML key omitted → null, or an empty map).
_Avoid_: missing scaling, no rules

**Inactive scaling window**:
The dictionary is non-empty but no entry is active for the current UTC instant per `ConfigFinder`.
_Avoid_: no scaling configured

## Overview

`ResourceProcessor` runs each due resource: refresh ARM state, push custom metrics when due, then evaluate active scaling configurations. Operators may omit scaling while keeping `CustomMetrics`.

## How to use

- Entry point is `ProcessOneAsync`: skip when disabled, not due, or `Enabled == false`; honor `IsDisabled()` before `RunLoop` and emit throttled disabled Information via `LogDisabledInformationIfDue`.
- In `RunLoop`, always call `Refresh` then `PushIfDueAsync` before touching `ScalingConfigurations`.
- When `HasNoScalingConfigurations` (null or empty), log absent-config Information at most once per hour (`LastNoScalingConfigurationsMessageLogged`), then return without scaling evaluation.
- When the dictionary exists but `GetActiveScalingConfigurations` is empty, Trace only (`No scaling configurations apply right now.`) and return without scaling evaluation.
- After active configurations exist, check `IsDisabled()` again before dimension/strategy work so disables discovered during refresh still skip scaling.

## Key files

- `autoscaler/resourcemanagement/ResourceProcessor.cs`
- `autoscaler/resourcemanagement/ResourceState.cs`
- `autoscaler/configuration/Resource.cs`
- `autoscaler/configuration/Configuration.cs`
- `autoscalertests/ResourceProcessorTests.cs`

## Gotchas

- Null and empty dictionary share the hourly absent-config Information path; inactive time windows use Trace only.
- Unhandled exceptions in `ProcessOneAsync` set a one-hour disable entry; without the early `IsDisabled()` guard, the next due cycle repeats the failure every `Frequency`.
- Do not read `ScalingConfigurations.Values` before refresh and custom-metrics push; null bind is valid at validation time.
