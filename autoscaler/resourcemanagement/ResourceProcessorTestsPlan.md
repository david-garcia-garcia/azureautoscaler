# ResourceProcessor Test Plan

This document outlines the test strategy for the `ResourceProcessor` component, which is central to the autoscaler's operation. It evaluates scaling rules, applies cooldowns, and drives dimension changes on discovered resources.

---

## 1. Behaviors to Test

### 1.1 Disabled Resource Is Not Evaluated

| Scenario | Expected Behavior |
|----------|-------------------|
| `resourceState.Configuration.Enabled == false` | `ProcessOneAsync` returns `false` immediately; `RunLoop` is never invoked |
| `resourceState.Configuration.Enabled == true` | Processing proceeds (subject to other guards) |

**Source:** `ResourceProcessor.ProcessOneAsync` lines 60–65.

---

### 1.2 Evaluation Frequency (NextEvaluationSeconds)

| Scenario | Expected Behavior |
|----------|-------------------|
| `NextEvaluationSeconds() > 0` (e.g., last evaluation was recent) | `ProcessOneAsync` returns `false`; no `RunLoop` |
| `NextEvaluationSeconds() <= 0` or `LastEvaluation == null` | Processing proceeds |

**Source:** `ResourceProcessor.ProcessOneAsync` lines 66–70.

---

### 1.3 ScaleDownCooldownSeconds

| Scenario | Expected Behavior |
|----------|-------------------|
| Rule wants scale down, `LastScale` was < `ScaleDownCooldownSeconds` ago | Scale down is skipped; rule continues to next rule |
| Rule wants scale down, `LastScale` was >= `ScaleDownCooldownSeconds` ago (or `LastScale == null`) | Scale down is allowed |
| Rule wants scale up | Cooldown check does not apply |

**Source:** `ResourceProcessor.RunLoop` lines 206–215.

---

### 1.4 ScaleDownLockWindowMinutes

| Scenario | Expected Behavior |
|----------|-------------------|
| `ScaleDownLockWindowMinutes` set, `DateTime.UtcNow.Minute < ScaleDownLockWindowMinutes` | Scale down is skipped (not allowed before minute X of the billable hour) |
| `ScaleDownLockWindowMinutes` set, `DateTime.UtcNow.Minute >= ScaleDownLockWindowMinutes` | Scale down is allowed (subject to other checks) |
| `ScaleDownLockWindowMinutes` not set | Window check is skipped |

**Source:** `ResourceProcessor.RunLoop` lines 216–225.

---

### 1.5 ScaleUpCooldownSeconds

| Scenario | Expected Behavior |
|----------|-------------------|
| Rule wants scale up, `LastScale` was < `ScaleUpCooldownSeconds` ago | Scale up is skipped |
| Rule wants scale up, `LastScale` was >= `ScaleUpCooldownSeconds` ago (or `LastScale == null`) | Scale up is allowed |
| Rule wants scale down | Cooldown check does not apply |

**Source:** `ResourceProcessor.RunLoop` lines 229–238.

---

### 1.6 ScaleUpAllowWindowMinutes

| Scenario | Expected Behavior |
|----------|-------------------|
| `ScaleDownLockWindowMinutes` set, `DateTime.UtcNow.Minute > ScaleUpAllowWindowMinutes` | Scale up is skipped (not allowed after minute Y of the billable hour) |
| `ScaleDownLockWindowMinutes` set, `DateTime.UtcNow.Minute <= ScaleUpAllowWindowMinutes` | Scale up is allowed (subject to other checks) |
| `ScaleDownLockWindowMinutes` not set | Window check is skipped |

**Source:** `ResourceProcessor.RunLoop` lines 240–247.

---

### 1.7 Resource IsDisabled (Post-Refresh)

| Scenario | Expected Behavior |
|----------|-------------------|
| `state.IsDisabled()` returns `true` (e.g., tag `autoscaler.disabled`, or transient disable) | `RunLoop` exits early after Refresh; no rule evaluation |
| `state.IsDisabled()` returns `false` | Processing continues |

**Source:** `ResourceProcessor.RunLoop` lines 117–127.

---

### 1.8 No Active Scaling Configuration

| Scenario | Expected Behavior |
|----------|-------------------|
| No scaling configurations match `SettingIsActive` for current time | `RunLoop` returns early; no Refresh, no rule evaluation |
| At least one scaling configuration is active | Processing continues |

**Source:** `ResourceProcessor.RunLoop` lines 102–110.

---

### 1.9 Invalid Metrics Skip Configuration

| Scenario | Expected Behavior |
|----------|-------------------|
| One or more metrics in a scaling configuration are invalid | That configuration is skipped; other configurations still evaluated |
| All metrics valid | Configuration is evaluated |

**Source:** `ResourceProcessor.RunLoop` lines 170–176.

---

### 1.10 Exception Handling

| Scenario | Expected Behavior |
|----------|-------------------|
| `ResourceNotFoundException` thrown during processing | Logged as warning; `ran` remains `false`; resource not disabled |
| Unhandled exception | Resource disabled for 1 hour; error logged |
| `ResetEvaluation` | Always called in `finally` so `NextEvaluationSeconds` is updated |

**Source:** `ResourceProcessor.ProcessOneAsync` lines 77–95.

---

## 2. Test Strategy Overview

### 2.1 Preferred Approach: Injectable Metrics Service

The main bottleneck for unit testing `ResourceProcessor` is **metric gathering**, which today:

- Creates `MetricsQueryClient` and `MetricEvaluation` inside `GatherMetrics`
- Calls Azure Monitor for non–custom metrics

**Recommendation:** Extract an `IMetricsGatherer` (or similar) and inject it into `ResourceProcessor`. Tests can provide a mock that returns fixed metric data. This:

1. Avoids needing a mock/dummy resource for metrics
2. Allows using a **real resource type** (e.g. `AzureDevOpsParallelJobsResourceState`) with already-mockable dependencies
3. Keeps tests fast and deterministic

### 2.2 Resource Choice for Integration-Style Tests

- **AzureDevOpsParallelJobsResourceState** is the best candidate:
  - Uses **custom metrics** (`custom_azdo_*`) only, which avoid `MetricsQueryClient` when `IMetricsGatherer` is mocked
  - Has internal test constructor accepting a mock `AzureDevOpsClient`
  - `Refresh` and `ApplyChanges` go through `AzureDevOpsClient`, which can be mocked via `HttpClient`
  - Dimensions (`DimensionAzureDevOpsHostedParallelJobs`, etc.) are simple and well-tested

- **MssqlElasticPoolResourceState** and similar Azure resources:
  - Depend on `ArmClient` and Azure Monitor for refresh and metrics
  - Harder to mock without a full “test resource” abstraction
  - Can be targeted later if needed

### 2.3 Mock vs Real Components

| Component | Strategy |
|-----------|----------|
| Metrics | Inject `IMetricsGatherer` mock; return predefined `Dictionary<string, MetricEvalDtoResult>` |
| ARM / Azure DevOps | Use `AzureDevOpsParallelJobsResourceState` with mocked `AzureDevOpsClient` (HttpClient) |
| Time (`DateTime.UtcNow`) | Inject `IDateTimeProvider` or `IClock` so tests can control “now” for cooldown/window checks |
| TokenCredential / ArmClient | Use `Mock<TokenCredential>`; not exercised if we avoid Azure Monitor and ARM |
| Dimensions | Use real dimensions (e.g. HostedParallelJobs); they are pure logic |
| LicenseInfo | Use `LicenseInfo` with `IsRestricted = false` to avoid 1‑minute delay in tests |

---

## 3. Refactoring Required for Testability

### 3.1 Metrics Service (IMetricsGatherer)

1. Define interface:

   ```csharp
   internal interface IMetricsGatherer
   {
       Task<Dictionary<string, MetricEvalDtoResult>> GatherMetricsAsync(
           ScalingConfiguration setting,
           ResourceState state,
           CancellationToken cancellationToken,
           ILogger logger);
   }
   ```

2. Create `AzureMonitorMetricsGatherer` implementing this interface (move current `GatherMetrics` logic there).
3. Add optional `IMetricsGatherer` parameter to `ResourceProcessor` constructor; default to `AzureMonitorMetricsGatherer` if not provided.
4. In tests, inject a mock that returns predefined metrics.

### 3.2 Time Provider (Optional but Recommended)

For `ScaleDownLockWindowMinutes`, `ScaleUpAllowWindowMinutes`, and cooldowns:

1. Define `IDateTimeProvider` with `DateTime UtcNow { get; }`.
2. Use it in `ResourceProcessor` instead of `DateTime.UtcNow`.
3. Inject a test implementation that returns a fixed or controllable time.

Without this, tests would depend on real system time, making them flaky.

### 3.3 ConfigFinder / SettingIsActive

`ConfigFinder` uses `DateTime.UtcNow` via `SettingIsActive`. Options:

- Inject `IDateTimeProvider` into `ConfigFinder`, or
- Create scaling configurations with `TimeWindow` “All” so `SettingIsActive` always returns true in tests.

The second option is simpler for initial tests; the first is better long-term.

---

## 4. Test File Layout

```
autoscalertests/
  ResourceProcessorTests.cs          # Main test class
  ResourceProcessorTestHelpers.cs    # Shared setup (mock resource, config, etc.)
```

---

## 5. Test Cases (Checklist)

### Phase 1: Early Returns (No RunLoop)

| # | Test Name | Description |
|---|-----------|-------------|
| 1 | `ProcessOneAsync_WhenResourceDisabled_ReturnsFalseAndDoesNotRunLoop` | `Enabled == false` |
| 2 | `ProcessOneAsync_WhenNextEvaluationSecondsPositive_ReturnsFalseAndDoesNotRunLoop` | Frequency not yet due |
| 3 | `ProcessOneAsync_WhenResourceEnabledAndEvaluationDue_RunsLoopAndReturnsTrue` | Happy path for “should process” |

### Phase 2: Cooldowns and Time Windows

These require `IDateTimeProvider` (or equivalent) to control `DateTime.UtcNow`.

| # | Test Name | Description |
|---|-----------|-------------|
| 4 | `RunLoop_ScaleDown_WhenWithinScaleDownCooldown_SkipsScaleDown` | `LastScale` recent, rule wants scale down |
| 5 | `RunLoop_ScaleDown_WhenBeyondScaleDownCooldown_AllowsScaleDown` | `LastScale` old enough |
| 6 | `RunLoop_ScaleDown_WhenWithinScaleDownLockWindow_SkipsScaleDown` | Minute < `ScaleDownLockWindowMinutes` |
| 7 | `RunLoop_ScaleDown_WhenBeyondScaleDownLockWindow_AllowsScaleDown` | Minute >= `ScaleDownLockWindowMinutes` |
| 8 | `RunLoop_ScaleUp_WhenWithinScaleUpCooldown_SkipsScaleUp` | `LastScale` recent, rule wants scale up |
| 9 | `RunLoop_ScaleUp_WhenBeyondScaleUpCooldown_AllowsScaleUp` | `LastScale` old enough |
| 10 | `RunLoop_ScaleUp_WhenAfterScaleUpAllowWindow_SkipsScaleUp` | Minute > `ScaleUpAllowWindowMinutes` |
| 11 | `RunLoop_ScaleUp_WhenWithinScaleUpAllowWindow_AllowsScaleUp` | Minute <= `ScaleUpAllowWindowMinutes` |

### Phase 3: Disabled State and Configuration

| # | Test Name | Description |
|---|-----------|-------------|
| 12 | `RunLoop_WhenStateIsDisabled_ExitsEarlyWithoutApplyingChanges` | `state.IsDisabled()` true after Refresh |
| 13 | `RunLoop_WhenNoActiveScalingConfigurations_ReturnsEarly` | No config matches `SettingIsActive` |
| 14 | `RunLoop_WhenInvalidMetrics_SkipsConfiguration` | One config has invalid metrics; others still run |

### Phase 4: Exception Handling

| # | Test Name | Description |
|---|-----------|-------------|
| 15 | `ProcessOneAsync_WhenResourceNotFoundException_CatchesAndReturnsFalse` | Handles `ResourceNotFoundException` |
| 16 | `ProcessOneAsync_WhenUnhandledException_DisablesResourceAndReturnsFalse` | Sets `DisabledUntil` for 1 hour |
| 17 | `ProcessOneAsync_AlwaysCallsResetEvaluationInFinally` | `LastEvaluation` is reset even on exception |

---

## 6. Implementation Order

1. **Refactor: Add `IMetricsGatherer`**  
   - Extract metrics logic to `AzureMonitorMetricsGatherer`  
   - Inject into `ResourceProcessor` (optional parameter, default to real implementation)

2. **Refactor: Add `IDateTimeProvider` (optional)**  
   - For reliable cooldown and window tests

3. **Add `ResourceProcessorTestHelpers`**  
   - Create `AzureDevOpsParallelJobsResourceState` with mocked `AzureDevOpsClient`  
   - Create scaling config with custom metrics, Fixed strategy, `TimeWindow` “All”  
   - Create `MockMetricsGatherer` returning predefined metrics

4. **Implement Phase 1 tests**  
   - Disabled resource, evaluation frequency, basic “runs when enabled” case

5. **Implement Phase 2 tests**  
   - Cooldowns and time windows (with `IDateTimeProvider`)

6. **Implement Phase 3 and Phase 4 tests**  
   - Disabled state, config filtering, exception handling

---

## 7. Dependencies

- **Moq** – already used in `autoscalertests`
- **InternalsVisibleTo** – already configured for `poolautoscaler.tests`
- **xUnit** – already in use

---

## 8. Notes on ScaleDownLockWindowMinutes vs ScaleUpAllowWindowMinutes

`ScaleDownLockWindowMinutes` and `ScaleUpAllowWindowMinutes` are both part of the “billable hour” logic:

- **Scale down** is blocked when the current minute is **less than** `ScaleDownLockWindowMinutes` (e.g. before minute 50).
- **Scale up** is blocked when the current minute is **greater than** `ScaleUpAllowWindowMinutes` (e.g. after minute 58).

Both checks are gated by `ScaleDownLockWindowMinutes.HasValue`. Tests should cover:

- Both `null` (no billable-hour logic)
- Both set, with various `DateTime.UtcNow.Minute` values

---

## 9. Alternative: Minimal Test Resource

If introducing `IMetricsGatherer` is delayed, a **minimal test-only resource** could be added:

- Extends `ResourceState`
- `Refresh` no-op or minimal
- `CustomMetric` returns configurable values
- `PreparePatch` / `ApplyChanges` no-op
- Paired with a minimal `IDimension` implementation

This would require more code and maintenance. The injectable metrics approach is preferred because it:

- Keeps production code cleaner
- Reuses real resource types
- Focuses mocking on the external boundary (metrics) rather than inventing a fake resource
