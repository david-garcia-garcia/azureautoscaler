## ADDED Requirements

### Requirement: Storage ScaleTarget formula uses the maximum across all metric window buckets
The `MaxDataBytes` storage rule's `ScaleTarget` expression SHALL derive the base storage value by taking the maximum `allocated_data_storage` across all metric window buckets (`.Values.Select(v => v.Default.Value).Max()`), not by reading only the most-recent bucket (`.Values.First()`). This ensures that a single anomalous low reading in the most-recent bucket cannot drive a scale-down decision.

#### Scenario: Single anomalous low bucket does not drive scale-down
- **WHEN** `allocated_data_storage` returns multiple metric buckets, and the most-recent bucket contains an anomalously low value (e.g., 3 GB) while earlier buckets contain the true value (e.g., 200 GB)
- **THEN** the `ScaleTarget` formula produces a storage target based on 200 GB (the window maximum), not 3 GB

#### Scenario: Normal operation uses the maximum observed storage
- **WHEN** all metric buckets return consistent values for `allocated_data_storage` (e.g., all around 200 GB)
- **THEN** the `ScaleTarget` formula produces the same result as it would with `.Values.First()` because the max equals the most-recent value

#### Scenario: Empty metric window is handled safely
- **WHEN** no metric buckets are returned (metric unavailable)
- **THEN** the `ValidValueMin` threshold causes the metric to be rejected and the evaluation is skipped for this cycle

### Requirement: `allocated_data_storage` metric window is at least 15 minutes
The `allocated_data_storage` metric used by the `MaxDataBytes` storage rule SHALL be configured with a `Window` of at least `00:15` (15 minutes). A 15-minute window provides enough buckets to absorb a single transient anomalous reading without it being the sole input to the formula.

#### Scenario: Short transient anomaly within 15-minute window is absorbed
- **WHEN** one metric bucket within a 15-minute window returns an anomalously low value
- **THEN** the remaining buckets in the window contain representative values, and `.Max()` over the window returns the correct peak

### Requirement: `allocated_data_storage` minimum valid value rejects readings below 1 GB
The `allocated_data_storage` metric's `ValidValueMin` SHALL be set to at least `1073741824` (1 GB). Readings below this threshold are treated as broken and discarded, preventing sub-gigabyte anomalies from reaching the `ScaleTarget` formula.

#### Scenario: Sub-gigabyte metric value is rejected
- **WHEN** Azure Monitor returns an `allocated_data_storage` value below 1 GB for a bucket
- **THEN** the metric evaluation discards that value (treats it as invalid), and it does not contribute to the `.Max()` computation

### Requirement: Storage scale-down is suppressed when `CurrentUsedStorage` reports zero
In `MssqlElasticPoolResourceState.PreparePatch`, when `CurrentUsedStorage` is `0` and the pool has a known `ExistingMssqlElasticPoolState.MaxSizeBytes` greater than zero, the system SHALL NOT allow `patch.MaxSizeBytes` to be reduced below the current `ExistingMssqlElasticPoolState.MaxSizeBytes`. A warning log entry SHALL be emitted identifying the suppression.

#### Scenario: Zero CurrentUsedStorage with existing MaxSizeBytes suppresses scale-down
- **WHEN** `InternalRefreshAsync` sets `CurrentUsedStorage` to `0` (e.g., because `storage_used` metric returned no values)
- **AND** `ExistingMssqlElasticPoolState.MaxSizeBytes` is greater than zero
- **AND** the scaling rules produce a `MaxSizeBytes` target lower than the existing `MaxSizeBytes`
- **THEN** `PreparePatch` overrides the target to `ExistingMssqlElasticPoolState.MaxSizeBytes`
- **AND** a warning-level log entry is emitted indicating `CurrentUsedStorage=0` suppressed a storage reduction

#### Scenario: Zero CurrentUsedStorage does not suppress scale-up
- **WHEN** `CurrentUsedStorage` is `0`
- **AND** the scaling rules produce a `MaxSizeBytes` target higher than the existing `MaxSizeBytes`
- **THEN** `PreparePatch` allows the scale-up to proceed normally (the guard only prevents decreases)

#### Scenario: Non-zero CurrentUsedStorage preserves existing behavior
- **WHEN** `CurrentUsedStorage` is greater than zero
- **THEN** `PreparePatch` applies the existing floor logic (`FindClosesValidStorageSize(currentStorage)`) and the guard does not apply
