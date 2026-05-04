## Context

- Background scale applies run inside `Task.Run` in `ResourceProcessor` after a patch is prepared; `resState.Logger` produces resource-scoped log prefixes (e.g. pool name and index).
- Successful completion is currently logged with a fixed message after `ApplyChanges` returns and `LastScale` is updated (`openspec/knowledge/staging/internal/scale-operation-background-logging.md`).

## Goals / Non-Goals

**Goals:**

- Include elapsed wall-clock time for the apply in the success log line, formatted as `HH:mm:ss` where the first field is **total whole hours** of the span (so multi-day runs do not silently wrap).
- Keep log level and general semantics (successful completion) unchanged.

**Non-Goals:**

- Duration on cancelled or failed paths (only the success line in scope unless product later asks).
- Sub-second precision (format is second granularity).
- New metrics, App Insights events, or dashboards.

## Decisions

1. **Time source** — Record `startUtc = getUtcNow()` immediately before `await resState.ApplyChanges(...)` (or immediately after the “Starting scale operation” line). Compute `elapsed = getUtcNow() - startUtc` after `ApplyChanges` completes. Reuses the same `getUtcNow` delegate already injected on the processor (consistent with `LastScale`).

2. **Format** — Build a display string `HH:mm:ss` using total hours and the `TimeSpan` minute/second components: `(int)elapsed.TotalHours`, `elapsed.Minutes`, `elapsed.Seconds`, each zero-padded to two digits. **Do not** use `TimeSpan.ToString(@"hh\:mm\:ss")` alone; for spans ≥ 24h the `hh` segment resets and misrepresents duration.

3. **Logging API** — Use `LogInformation` with a message template and a dedicated placeholder, e.g. `"Scale operation completed successfully after {Elapsed}"` where `Elapsed` is the formatted string (structured field remains queryable). Adjust wording minimally so existing “Scale operation completed successfully” intent is obvious.

4. **Tests** — If there are unit/integration tests that assert the exact log message, update expected text or switch assertions to structured property checks where appropriate.

## Risks / Trade-offs

- **[Risk] Fragile log substring alerts** — Mitigation: document the new template; prefer structured `Elapsed` for monitors.
- **[Risk] Very long runs** — Mitigation: `TotalHours` as int can exceed 99; format still valid (e.g. `100:05:03`). If product wants a cap, that would be a follow-up.

## Migration Plan

- Deploy with code change only; no data migration. Rollback is revert commit.

## Open Questions

- None for MVP; confirm with stakeholders if cancel/failure logs should also include partial duration.
