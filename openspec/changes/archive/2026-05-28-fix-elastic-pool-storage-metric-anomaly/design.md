## Context

The Azure SQL Elastic Pool autoscaler evaluates `allocated_data_storage` metric values via `AzureMonitorMetricsGatherer`, reverses them newest-first, and then the YAML `ScaleTarget` expression operates on them. `MssqlElasticPoolResourceState.PreparePatch()` assembles the patch, including an existing floor guard based on `CurrentUsedStorage`. `MssqlElasticPoolResourceState.ApplyChanges()` submits the patch via the ARM SDK and re-raises known transient errors as `TransientAzureOperationException`.

The incident exposed two compounding gaps:

1. **Formula** — `ScaleTarget` used `.Values.First().Default.Value` (single most-recent bucket). When Azure Monitor returned a spurious low value for that single bucket (e.g. a few GB), the formula produced a target far below actual allocated storage — and the existing floor was bypassed because `CurrentUsedStorage` was also zero from the `storage_used` metric failure.
2. **Recovery** — Once `MaxSizeBytes` was below actual data, any recovery patch that did not exceed the actual data was rejected with `ElasticPoolDecreaseStorageLimitBelowUsage`. With the formula based on capped `CurrentUsedStorage`, it could never compute a sufficient target in one shot. The error was unhandled, triggering a 1-hour resource disable rather than a short retry.

## Goals / Non-Goals

**Goals:**
- Prevent a single anomalous metric reading from driving a storage scale-down.
- Block storage reductions when `CurrentUsedStorage` is zero (metric failure indicator).
- Enable automatic recovery from the stuck `MaxSizeBytes < actual data` state without human intervention.
- Document the metric-anomaly risk and protective configuration in the SQL elastic pool docs.

**Non-Goals:**
- Changing the general scaling evaluation loop or scheduling.
- Extending these guards to other resource types (SQL database, AKS, etc.).
- Automated alerting or notification — logging a CRITICAL entry is sufficient.
- Querying `allocated_data_storage` from ARM / Azure Portal as an authoritative override of the Azure Monitor value.

## Decisions

### D1 — Formula: `Max()` over the window instead of `First()`

**Decision**: Change the `ScaleTarget` expression to iterate all metric buckets with `.Values.Select(v => v.Default.Value).Max()` rather than `.Values.First().Default.Value`.

**Rationale**: `Aggregations: ["Maximum"]` only aggregates *within* each time bucket; `Values.First()` then throws away all but the most recent. Using `.Max()` across all buckets in the window means a single anomalous bucket at any position in the window cannot determine the outcome.

**Alternatives considered**:
- Use `Values.Average()` — rejected; average is inappropriate for a floor calculation (data could grow suddenly; you never want to provision below observed peak).
- Use `Values.Last()` / oldest bucket — rejected; same single-point exposure as `First()`.
- Extend the window to hours — rejected; the window increase (5 → 15 min) is defense-in-depth, not the primary fix. A multi-hour window would also slow legitimate downscaling responses.

### D2 — `CurrentUsedStorage = 0` guard in `PreparePatch`

**Decision**: Before computing the final `patch.MaxSizeBytes`, if `currentStorage == 0` and `existingMaxSizeBytes > 0`, clamp the target at `existingMaxSizeBytes` (no downscale) and log a warning.

**Rationale**: A pool with any databases will never legitimately have zero bytes in use. Zero means the `storage_used` metric failed. The safest policy when a measurement is unavailable is to not reduce the resource.

**Alternatives considered**:
- Abort the entire patch — rejected; DTU and per-db-max changes may still be valid. Only suppressing the storage reduction is more surgical.
- Use `allocated_data_storage` metric from `data.Metrics` instead — rejected; `PreparePatch` operates on `ExistingMssqlElasticPoolState` populated by `InternalRefreshAsync`, which only queries `storage_used`. Adding a second metric read there is a larger refactor outside scope.

### D3 — Incremental 50 GB bump for stuck-pool recovery

**Decision**: Add a private `_storageRecoveryBumpBytes` field (`long`, initial value `0`) to `MssqlElasticPoolResourceState`. In `PreparePatch`, after computing `patch.MaxSizeBytes`, add this bump. In `ApplyChanges`, when `ElasticPoolDecreaseStorageLimitBelowUsage` occurs on a storage scale-up, increment the bump by 50 GB and rethrow as `TransientAzureOperationException` (5-minute retry). Reset the bump in `PreparePatch` when `currentStorage > 0 && currentStorage < existingMaxSizeBytes * 0.95` (pool is back below capacity).

**Rationale**: The bump converges on the actual data size in ≤ 5 retry cycles (each 5 minutes apart, so recovery within ~25 minutes in the worst case). It avoids large, arbitrary one-shot multipliers and naturally stops once the patch is accepted. Resetting when the pool is healthy (< 95% capacity) is conservative and does not require modifying the base class or ResourceProcessor.

**Alternatives considered**:
- `× 3` multiplier — rejected by user; over-provisions by an arbitrary factor and gives no signal about how close the target is to the real data.
- Jump to pool-tier maximum (e.g., 1 TB) — over-provisions massively and may cause DTU-storage co-validation failures; also arbitrary.
- Storing the bump in a persistent log/DB — out of scope; in-process state is fine since the recovery cycle is minutes not days.
- Resetting in `ResourceProcessor` after success — requires modifying the base class API; the `PreparePatch` check is self-contained.

### D4 — `ElasticPoolDecreaseStorageLimitBelowUsage` as transient only on scale-up

**Decision**: In `ApplyChanges`, add a new catch clause for `ElasticPoolDecreaseStorageLimitBelowUsage` that checks `internalPatch.MaxSizeBytes > ExistingMssqlElasticPoolState.MaxSizeBytes`. If true (scale-up), increment bump, log CRITICAL, and throw `TransientAzureOperationException(5 min)`. If false (legitimate scale-down rejection), rethrow as-is to preserve 1-hour disable.

**Rationale**: The error has two distinct meanings: (a) a legitimate rejection of a downscale that would break the pool, and (b) a sign that the pool is already stuck and the recovery target is still below actual data. Treating both identically with a 1-hour disable prevents recovery in case (b). The scale-up/scale-down discriminator is `patch.MaxSizeBytes > existing.MaxSizeBytes`, which is always known at `ApplyChanges` time.

**Alternatives considered**:
- Always treat it as transient — rejected; would cause infinite short-retry loops on legitimate downscale rejections.
- Log and throw `OperationCanceledException` — rejected; disrupts resource processor flow.

## Risks / Trade-offs

- **Bump convergence time** — In the worst case (e.g., 200 GB deficit in 50 GB steps), recovery takes ~4 additional retries × 5 minutes = ~20 minutes. This is acceptable given the 1-hour status quo.

- **Bump not persisted** — If the autoscaler pod restarts mid-recovery, the bump resets to 0. The next evaluation starts from scratch. Under normal pod restarts (rolling update, OOM kill) the time-to-recovery is reset. Accepted: the `ApplyChanges` failure cycle restarts automatically; this is not significantly worse than the current 1-hour disable.

- **Formula change affects all pools sharing this config** — The `.Max()` change is strictly safer and is the correct semantics for "never go below the peak observed storage". No correctness concern.

- **`CurrentUsedStorage = 0` guard prevents legitimate downscale** — Unlikely in practice (a pool would have to genuinely have zero databases), but if it did occur, the guard would suppress the downscale for one evaluation cycle. Since `storage_used` is queried with a 1-minute window, the next refresh would typically return a non-zero value.

## Migration Plan

1. Deploy config change (formula, window, `ValidValueMin`) to a non-production environment and verify that metric resolution is correct with log assertions.
2. Deploy code change (`PreparePatch` guards, bump field, `ApplyChanges` transient handler) behind no feature flag — it is additive and non-breaking.
3. Update `docs/resources/sql-elastic-pool.md` in the same PR.
4. No rollback complexity — config changes are YAML; code changes are pure additive logic with no schema migrations.

## Open Questions

- None. Design is complete based on the investigation and user-approved direction.
