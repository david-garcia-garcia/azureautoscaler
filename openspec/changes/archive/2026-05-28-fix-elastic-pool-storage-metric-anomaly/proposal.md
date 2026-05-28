## Why

During a production incident, Azure Monitor `storage_used` and `allocated_data_storage` for an elastic pool both returned anomalously low values at the same time. The storage rule formula used the single most-recent metric bucket (`.Values.First()`), so one bad bucket drove the computed target far below the window peak and triggered an unsafe storage scale-down. Azure accepted the operation despite actual database data exceeding the new limit (a known Azure API silent-acceptance bug), placing `MaxSizeBytes` below actual data. The autoscaler detected the broken state quickly but could not self-recover because the capped metric prevented computing a sufficient recovery target, and the failure was treated as a generic 1-hour disable rather than a short-retry transient.

## What Changes

- **Formula fix**: Change `MaxDataBytes` storage rule from `.Values.First()` (single most-recent bucket) to `.Values.Select(v => v.Default.Value).Max()` (max across all buckets in the metric window), so a single anomalous bucket cannot drive the decision.
- **Metric window + threshold**: Extend `allocated_data_storage` window from 5 min to 15 min; raise `ValidValueMin` from 1 MB to 1 GB.
- **`CurrentUsedStorage = 0` guard**: In `PreparePatch`, suppress any storage reduction when `CurrentUsedStorage` is zero and a known `MaxSizeBytes` exists — zero means the `storage_used` metric returned nothing useful.
- **Incremental recovery bump**: In `PreparePatch`, track a per-resource-instance recovery counter. Each time `ElasticPoolDecreaseStorageLimitBelowUsage` is received on a storage scale-up, add 50 GB to the counter. On subsequent evaluations the counter is added to the formula result, converging on the true data size in ≤ 5 retry cycles.
- **`ElasticPoolDecreaseStorageLimitBelowUsage` on scale-up as transient**: Extend the transient error map to treat this error as a 5-minute retry when the patch is a storage *increase* (the pool-stuck scenario). Keep the existing 1-hour disable when the patch is a storage *decrease* (legitimate Azure rejection).
- **Documentation**: Add a warning section to `docs/resources/sql-elastic-pool.md` covering metric anomaly scenarios, recommended `Window` / `ValidValueMin` settings, and the safe formula pattern.

## Capabilities

### New Capabilities

- `elastic-pool-storage-metric-resilience`: Specification for how the autoscaler defends against transient Azure Monitor anomalies in `allocated_data_storage` and `storage_used` — covers formula requirements, metric window/threshold requirements, and the `CurrentUsedStorage = 0` suppression guard.
- `elastic-pool-stuck-storage-recovery`: Specification for recovery when the elastic pool ends up in a state where `MaxSizeBytes < actual data` — covers the incremental bump mechanism and the triggering / reset conditions.

### Modified Capabilities

- `transient-azure-error-short-disable`: Extend with the new scenario where `ElasticPoolDecreaseStorageLimitBelowUsage` received on a storage *increase* is treated as a short-retry transient (5 minutes) rather than a 1-hour unhandled-exception disable.

## Impact

- **Config file**: elastic-pool resource YAML under `autoscaler/resources/` (e.g. `sql-elastic-pool.resources.yml`) — formula, window, ValidValueMin.
- **Code**: `autoscaler/resources/MssqlElasticPool/MssqlElasticPoolResourceState.cs` — `PreparePatch` guards, recovery bump field, `ApplyChanges` transient catch.
- **Docs**: `docs/resources/sql-elastic-pool.md` — new warning section.
- **Tests**: `autoscalertests/MssqlElasticPoolResourceStateTests.cs` — new test cases for guards, bump, and transient behavior.
- No breaking changes to public configuration schema, API, or behavior under normal (healthy metric) conditions.
