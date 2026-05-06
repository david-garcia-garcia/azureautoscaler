## ADDED Requirements

### Requirement: TimeWindow is optional in ScalingConfiguration
A `ScalingConfiguration` SHALL be valid when `TimeWindow` is omitted from the YAML. When omitted, the configuration SHALL be treated as always active: all days, all months, `00:00`–`23:59` in UTC.

#### Scenario: Absent TimeWindow is always active
- **WHEN** a `ScalingConfiguration` has no `TimeWindow` key
- **THEN** `ConfigFinder.SettingIsActive` SHALL return `true` for any date and time

#### Scenario: Absent TimeWindow does not crash on startup
- **WHEN** `Configuration.PrepareAndValidate` is called with a `ScalingConfiguration` that has no `TimeWindow`
- **THEN** the method SHALL complete without throwing an exception

### Requirement: TimeWindow fields are individually optional
Each field of `TimeWindow` (`Days`, `Months`, `StartTime`, `EndTime`, `TimeZone`) SHALL be optional. Omitted fields SHALL default to their all-period value: `Days` = `"All"`, `Months` = `"All"`, `StartTime` = `"00:00"`, `EndTime` = `"23:59"`, `TimeZone` = `"UTC"`.

#### Scenario: Partial TimeWindow with only Days specified
- **WHEN** a `TimeWindow` specifies only `Days: Weekday` and omits all other fields
- **THEN** the time-of-day check SHALL match any time (00:00–23:59)
- **THEN** the month check SHALL match any month
- **THEN** the timezone SHALL be UTC

#### Scenario: Partial TimeWindow with only StartTime and EndTime specified
- **WHEN** a `TimeWindow` specifies only `StartTime` and `EndTime` and omits `Days` and `Months`
- **THEN** the day check SHALL match any day
- **THEN** the month check SHALL match any month
