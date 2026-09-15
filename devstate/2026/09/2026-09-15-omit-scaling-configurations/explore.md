# Explore
IssueKey: 2026-09-15-omit-scaling-configurations

## Concepts

**Null vs empty `ScalingConfigurations`.** YAML omit leaves `Resource.ScalingConfigurations` null after bind; an empty key yields an empty dictionary. `Configuration.PrepareAndValidate` skips scaling validation when null (valid). Runtime must not dereference `.Values` on null.

**RunLoop ordering today.** `ResourceProcessor.RunLoop` builds the active scaling-configuration list from `ScalingConfigurations.Values` before `Refresh` and `PushIfDueAsync`. Empty dictionary survives; null throws `NullReferenceException` before refresh. When the list is empty after time-window filtering, refresh and push already run, then Trace `"No scaling configurations apply right now."` and return.

**Disable path.** `ProcessOneAsync` calls `RunLoop` without an upfront `IsDisabled()` check. Unhandled exceptions set `DisabledUntil` with a one-hour expiry; `finally` always calls `ResetEvaluation()`. `RunLoop` checks `IsDisabled()` only after refresh/push and only when at least one scaling configuration is active — so a repeating NRE never reaches that guard. `LastDisabledMessageLogged` throttles disabled Information to once per hour inside that late guard.

**Custom metrics without scaling.** Operators can configure `CustomMetrics` (including Query sources) while omitting scaling blocks — metrics push is independent of scaling evaluation once refresh completes.

```
ProcessOneAsync
  Enabled? Frequency due?
       │
       ▼
  RunLoop ──► [today] .Values on null ──► NRE
       │              │
       │              └──► catch: 1h disable; ResetEvaluation
       ▼
  Refresh → PushIfDueAsync → active configs?
       │                           │
       no active / empty           yes → IsDisabled? → scale eval
       └── Trace (time window)
```

**Affected units**

| Path | Owns |
|------|------|
| `autoscaler/resourcemanagement/ResourceProcessor.cs` | Per-resource evaluation loop, refresh/push ordering, disable checks, scaling eval |
| `autoscaler/resourcemanagement/ResourceState.cs` | Disable map, evaluation timing, throttle timestamps on state |
| `autoscaler/configuration/Resource.cs` | Bound shape of `ScalingConfigurations` |
| `autoscaler/configuration/Configuration.cs` | Startup validation; null scaling configs allowed |
| `autoscalertests/ResourceProcessorTests.cs` | Behavioral tests; helpers build configs with empty dict, not null |

**Call-site survey (`ScalingConfigurations`).** Searched worktree `*.cs`: `ResourceProcessor.cs` (runtime `.Values`), `Configuration.cs` (validation loop), `Resource.cs` (property), `ResourceProcessorTests.cs` / `ConfigFinderTests.cs` (tests). No other production callers. Blast radius is bounded to the processor loop and tests.

## Decisions

1. **Null-as-empty after refresh.** Coalesce null to “no configurations” using the same exit path as an empty dictionary after `Refresh` and `PushIfDueAsync`, without requiring YAML validation changes (out of scope).

2. **Two log levels for two cases.** Information (hourly throttle): resource has no scaling configurations configured (null or empty dictionary). Trace (unchanged): dictionary exists but no entry is active for the current time window.

3. **Disable before work.** `ProcessOneAsync` returns before `RunLoop` when `IsDisabled()` is true, skipping refresh and scaling for that cycle. Extract the throttled disabled Information block so both the early return and the post-refresh check in `RunLoop` share one helper and `LastDisabledMessageLogged`.

4. **New throttle field.** Add a dedicated `ResourceState` timestamp for no-config Information (parallel to `LastDisabledMessageLogged`); no existing field covers that message.

5. **Tests.** Add null-configuration coverage via helper or explicit `ScalingConfigurations = null`; assert no throw and refresh runs. Add disable-honored-on-second-cycle test after a synthetic unhandled-exception disable. Use Moq `Verify` on the resource logger for Information when practical (suite already uses `Mock<ILogger>`).

## Open questions

- Q: Does `ResourceState` need a new last-logged field for the no-config Information throttle?
  Rank: additive asked — new state field; requirement **Desired** allows `ResourceState` change; **Unknowns** names this gap
  Decision: assumed — add a sibling property to `LastDisabledMessageLogged`; only disable throttle exists today (`ResourceState.cs`).
  By: explore

- Q: When `ProcessOneAsync` skips `RunLoop` because `IsDisabled()`, where does the once-per-hour disabled Information log run without duplicating spam?
  Rank: additive asked — **Desired** requires honor disable before `RunLoop` and reuse the RunLoop INFO pattern
  Decision: assumed — private throttled helper on `ResourceProcessor` invoked from early `ProcessOneAsync` return and from existing `RunLoop` post-refresh check.
  By: explore

- Q: Do null and empty dictionary share the no-config Information path, while inactive time windows keep Trace only?
  Rank: additive asked — **Desired** “Treat null like empty” and separates INFO vs Trace wording
  Decision: assumed — null and `{}` use hourly Information; non-empty dict with zero active windows keeps Trace-only early return.
  By: explore

- Q: Should `RunLoop` defer any `ScalingConfigurations.Values` access until after `Refresh`/`PushIfDueAsync`?
  Rank: additive asked — **Desired** requires refresh and push before scaling exit; current null NRE is pre-refresh
  Decision: assumed — yes; align null/empty handling with the existing empty-dict path after refresh/push.
  By: explore

- Q: How should tests assert no-config Information given current logger mocks?
  Rank: additive asked — **Desired** tests clause (“if the suite supports log capture”)
  Decision: assumed — use `Mock<ILogger>.Verify` for `LogInformation` on the resource logger; skip only if a test uses a non-mock logger without capture.
  By: explore

Verdict: in progress
