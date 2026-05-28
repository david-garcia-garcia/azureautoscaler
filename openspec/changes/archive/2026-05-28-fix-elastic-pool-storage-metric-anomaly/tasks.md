## 1. Config: fix storage ScaleTarget formula and metric parameters

- [x] 1.1 In the affected elastic-pool resource YAML under `autoscaler/resources/`, change the `MaxDataBytes` storage rule `ScaleTarget` expression: replace both occurrences of `.Values.First().Default.Value` with `.Values.Select(v => v.Default.Value).Max()`
- [x] 1.2 In the same file, change the `allocated_data_storage` metric `Window` from `00:05` to `00:15`
- [x] 1.3 In the same file, change the `allocated_data_storage` metric `ValidValueMin` from `1048576` (1 MB) to `1073741824` (1 GB)

## 2. Code: `CurrentUsedStorage = 0` guard in `PreparePatch`

- [x] 2.1 In `MssqlElasticPoolResourceState.PreparePatch`, after the existing `FindClosesValidStorageSize` floor is applied, add a guard: if `currentStorage == 0` and `existingMaxSizeBytes > 0` and `patch.MaxSizeBytes < existingMaxSizeBytes`, set `patch.MaxSizeBytes = existingMaxSizeBytes` and emit a `LogWarning` identifying the suppression

## 3. Code: incremental recovery bump in `MssqlElasticPoolResourceState`

- [x] 3.1 Add a private field `private long _storageRecoveryBumpBytes = 0L;` to `MssqlElasticPoolResourceState`
- [x] 3.2 In `PreparePatch`, at the start of the method (before the floor logic), reset `_storageRecoveryBumpBytes` to `0` and emit `LogInformation` when `currentStorage > 0` and `currentStorage < existingMaxSizeBytes * 0.95` and the bump is currently non-zero
- [x] 3.3 In `PreparePatch`, after the Guard 1 check and before the final `FindClosesValidStorageSize` tier-snap, if `_storageRecoveryBumpBytes > 0`, add it to `patch.MaxSizeBytes`

## 4. Code: `ElasticPoolDecreaseStorageLimitBelowUsage` as transient on scale-up in `ApplyChanges`

- [x] 4.1 In `MssqlElasticPoolResourceState.ApplyChanges`, add a new `catch` clause for `Azure.RequestFailedException` where `rfex.ErrorCode == "ElasticPoolDecreaseStorageLimitBelowUsage"` and `internalPatch.MaxSizeBytes > ExistingMssqlElasticPoolState.MaxSizeBytes`
- [x] 4.2 In that catch clause: increment `_storageRecoveryBumpBytes` by `50L * 1024L * 1024L * 1024L`, emit `LogCritical` describing the stuck state and current bump, then throw `new TransientAzureOperationException("ElasticPoolDecreaseStorageLimitBelowUsage", 5, rfex)`
- [x] 4.3 Verify that `ElasticPoolDecreaseStorageLimitBelowUsage` on a scale-*down* (patch `MaxSizeBytes` ≤ existing) still propagates to the generic handler (1-hour disable) with no code change needed — confirm by code inspection

## 5. Tests

- [x] 5.1 In `autoscalertests/MssqlElasticPoolResourceStateTests.cs`, add a test verifying that `PreparePatch` does not reduce `MaxSizeBytes` when `CurrentUsedStorage = 0` and existing `MaxSizeBytes > 0`
- [x] 5.2 Add a test verifying that `PreparePatch` allows a scale-up when `CurrentUsedStorage = 0` (guard does not block increases)
- [x] 5.3 Add a test verifying that after a `ElasticPoolDecreaseStorageLimitBelowUsage` on scale-up, `_storageRecoveryBumpBytes` is incremented by 50 GB and `TransientAzureOperationException` is thrown
- [x] 5.4 Add a test verifying that on the next `PreparePatch` call, the bump is added to the formula-derived `MaxSizeBytes`
- [x] 5.5 Add a test verifying that the bump is reset when `currentStorage < existingMaxSizeBytes * 0.95` on a subsequent `PreparePatch` call
- [x] 5.6 Add a test verifying that `ElasticPoolDecreaseStorageLimitBelowUsage` on a scale-down does NOT throw `TransientAzureOperationException` (falls through to generic)

## 6. Documentation

- [x] 6.1 In `docs/resources/sql-elastic-pool.md`, add a new warning section (e.g., `## Resilience to Azure Monitor metric anomalies`) explaining: (a) the risk of transient anomalous readings, (b) recommended `Window` (≥ 15 min), `ValidValueMin` (≥ 1 GB), and `.Max()` formula pattern, (c) the automatic stuck-pool recovery mechanism and the CRITICAL log entries operators should watch for
