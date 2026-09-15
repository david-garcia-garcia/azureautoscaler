## Why

Resources that define `CustomMetrics` but omit the `ScalingConfigurations` YAML key bind with a null dictionary and crash every evaluation cycle when `RunLoop` dereferences `.Values` before refresh. The resulting unhandled exception applies a one-hour disable, but the next cycle repeats the failure because disable is checked too late, flooding logs and blocking refresh and custom-metrics push.

## What Changes

- Treat null `ScalingConfigurations` like an empty dictionary after refresh and custom-metrics push: no scaling evaluation, no throw.
- Log Information once per hour per resource when scaling configurations are absent (null or empty), using the same resource identity as existing logs; keep Trace for “no configuration active in the current time window” when the dictionary exists but none apply.
- Honor `IsDisabled()` in `ProcessOneAsync` before entering `RunLoop`, sharing one throttled disabled Information helper with the post-refresh check in `RunLoop`.
- Add `ResourceState` throttle timestamp for the no-config Information message (parallel to `LastDisabledMessageLogged`).
- Reorder `RunLoop` so scaling-configuration enumeration does not run on null before refresh/push.
- Tests: null `ScalingConfigurations` (YAML-omit shape), refresh without throw, optional Information assert; second `ProcessOneAsync` within the disable hour skips failing work.

## Capabilities

### New Capabilities

- `core_resource_management_omit-scaling-configurations`: Per-resource evaluation when `ScalingConfigurations` is omitted or empty—refresh and push run, scaling is skipped safely, throttled logging, and disable honored before work.

### Modified Capabilities

_(none)_

## Impact

- `autoscaler/resourcemanagement/ResourceProcessor.cs` — `ProcessOneAsync` early disable, `RunLoop` ordering, shared throttled disabled log, null/empty scaling exit path.
- `autoscaler/resourcemanagement/ResourceState.cs` — no-config Information throttle field.
- `autoscalertests/ResourceProcessorTests.cs` — null config, disable-on-second-cycle coverage.
