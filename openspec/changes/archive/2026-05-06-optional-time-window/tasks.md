## 1. Model Changes

- [x] 1.1 In `TimeWindow.cs`, add default values: `Days = "All"`, `Months = "All"`, `StartTime = "00:00"`, `EndTime = "23:59"` (TimeZone already defaults to "UTC")
- [x] 1.2 In `ScalingConfiguration.cs`, initialise the `TimeWindow` property to `new TimeWindow()` so it is never null when the YAML key is absent

## 2. Test Coverage

- [x] 2.1 Add a test: `ConfigFinder_WhenTimeWindowIsNull_ReturnsTrue` — verify `SettingIsActive` returns `true` for any time when `TimeWindow` is absent (uses default)
- [x] 2.2 Add a test: `PrepareAndValidate_WhenTimeWindowIsAbsent_DoesNotThrow` — verify startup does not throw when `ScalingConfiguration` has no `TimeWindow` in config
- [x] 2.3 Add a test: `ConfigFinder_WhenTimeWindowHasOnlyDays_MatchesAllTimesAndMonths` — verify partial `TimeWindow` (only `Days`) matches any time and month

## 3. Documentation

- [x] 3.1 In `docs/configuration.md`, add a note that `TimeWindow` is optional and defaults to all-day UTC when omitted; show the minimal example with no `TimeWindow`

## 4. Verify

- [x] 4.1 Run all tests and confirm they pass
