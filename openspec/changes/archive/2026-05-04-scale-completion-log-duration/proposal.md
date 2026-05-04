## Why

Operators rely on completion logs to correlate scale runs with incidents and Azure activity. Today the success line does not say how long the operation took, so triage requires guessing or cross-referencing other telemetry. Adding an explicit elapsed duration makes each scale cycle self-describing in log streams.

## What Changes

- Extend the informational log emitted when a background scale operation finishes successfully so it includes elapsed time formatted as `HH:mm:ss` (total hours, not clock time).
- Use a duration-safe format so spans longer than 24 hours still render correctly (total hours in the first segment).

## Capabilities

### New Capabilities

- `scale-operation-completion-log`: Requirements for the text and structured fields of scale completion (and related) log lines when a background apply finishes.

### Modified Capabilities

<!-- No existing specs in openspec/specs/. -->

## Impact

- **Code**: `autoscaler/resourcemanagement/ResourceProcessor.cs` — background `Task.Run` that calls `ApplyChanges` and logs `Scale operation completed successfully`.
- **Logging**: Same category/level; message template gains a duration placeholder. Downstream log queries that match the exact old string may need updating if they are brittle.
- **Knowledge**: Internal research staged at `openspec/knowledge/staging/internal/scale-operation-background-logging.md` (flow, file location, duration formatting note).
