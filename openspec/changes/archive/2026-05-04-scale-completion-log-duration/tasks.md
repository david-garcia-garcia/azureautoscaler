## Documentation / reading order (implementation)

Read before coding:

1. `openspec/changes/scale-completion-log-duration/proposal.md` — intent and scope
2. `openspec/changes/scale-completion-log-duration/design.md` — time capture, duration format, logging shape
3. `openspec/changes/scale-completion-log-duration/specs/scale-operation-completion-log/spec.md` — acceptance criteria
4. `openspec/knowledge/staging/internal/scale-operation-background-logging.md` — code location and flow notes

## 1. Implementation

- [x] 1.1 In `autoscaler/resourcemanagement/ResourceProcessor.cs`, capture UTC start time before `await resState.ApplyChanges` in the background scale task (same `getUtcNow` as existing `LastScale` usage).
- [x] 1.2 After `ApplyChanges` completes successfully, compute elapsed `TimeSpan` and format duration as `HH:mm:ss` using total whole hours plus minute and second components (see design — avoid `hh` TimeSpan format alone).
- [x] 1.3 Update the success `LogInformation` call to include the formatted duration with a structured template property (e.g. `{Elapsed}`).

## 2. Verification

- [x] 2.1 Run/build tests; update or add assertions if any test fixes the exact completion log string.
- [x] 2.2 Manually confirm a sample log line reads like success text plus `after 00:01:23` (or similar) for a short run.
