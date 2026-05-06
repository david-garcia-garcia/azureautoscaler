## Why

Most scaling configurations are active all day, yet the YAML requires users to spell out the full `TimeWindow` block every time. This creates noise in configurations and an easy source of errors (e.g. forgetting `EndTime` causes a crash). Making `TimeWindow` optional with an all-day default removes this friction.

## What Changes

- `TimeWindow` can be omitted entirely from a `ScalingConfiguration`; when absent it defaults to all days, all months, `00:00`–`23:59` UTC.
- Individual `TimeWindow` fields (`Days`, `Months`, `StartTime`, `EndTime`, `TimeZone`) can be omitted; each defaults to its all-day/all-period value.
- Test coverage added for the new default behaviour.
- Documentation updated to reflect that `TimeWindow` is optional.

## Capabilities

### New Capabilities

- `optional-time-window`: Allow `TimeWindow` to be omitted (or partially specified) in a `ScalingConfiguration`, defaulting to an always-active (all-day, all-days, all-months, UTC) window.

### Modified Capabilities

## Impact

- `autoscaler/configuration/TimeWindow.cs` — add default values for `Days`, `Months`, `StartTime`, `EndTime`
- `autoscaler/configuration/ScalingConfiguration.cs` — initialise `TimeWindow` property to `new TimeWindow()`
- `autoscalertests/` — new tests for absent and partial `TimeWindow`
- `docs/configuration.md` — note that `TimeWindow` is optional
