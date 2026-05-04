## ADDED Requirements

### Requirement: Successful scale completion log includes elapsed duration

When a background scale operation completes successfully, the autoscaler SHALL emit an informational log that states the operation completed successfully and includes the elapsed time from the start of that apply until completion, formatted as `HH:mm:ss`, where the first segment is the total whole number of hours in the elapsed span (not a 24-hour clock).

#### Scenario: Apply finishes after measurable elapsed time

- **WHEN** a background scale apply starts and later completes without error
- **THEN** the success log line MUST include a duration string matching the pattern `H+:MM:SS` (one or more hour digits, two-digit minutes, two-digit seconds) derived from UTC elapsed time between apply start and apply completion

#### Scenario: Apply spans more than twenty-four hours

- **WHEN** the elapsed time between apply start and completion is 25 hours or more
- **THEN** the hour segment of the duration string MUST reflect total hours (e.g. `25:00:00` for twenty-five hours), not a value that wraps within 0–23
