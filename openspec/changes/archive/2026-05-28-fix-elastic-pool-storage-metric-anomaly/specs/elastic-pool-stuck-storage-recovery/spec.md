## ADDED Requirements

### Requirement: Pool stuck below actual data size triggers incremental recovery
When an `ElasticPoolDecreaseStorageLimitBelowUsage` error is received during a storage *scale-up* patch (target `MaxSizeBytes` > existing `MaxSizeBytes`), `MssqlElasticPoolResourceState` SHALL accumulate a per-instance recovery bump in increments of 50 GB and apply this bump to subsequent `MaxSizeBytes` targets until the patch is accepted by Azure.

#### Scenario: First recovery attempt rejected — bump is initialized and incremented
- **WHEN** `ApplyChanges` submits a storage scale-up patch
- **AND** Azure returns `ElasticPoolDecreaseStorageLimitBelowUsage` (target is still below actual data)
- **THEN** the instance's `_storageRecoveryBumpBytes` is incremented by 50 GB
- **AND** the error is rethrown as `TransientAzureOperationException` with a 5-minute retry window
- **AND** a CRITICAL-level log entry is emitted indicating the pool is in a stuck state, the current bump value, and that automatic recovery is in progress

#### Scenario: Subsequent recovery attempt includes accumulated bump
- **WHEN** `PreparePatch` is called on the next evaluation cycle after a failed recovery attempt
- **AND** `_storageRecoveryBumpBytes` is greater than zero
- **THEN** the computed `patch.MaxSizeBytes` is increased by `_storageRecoveryBumpBytes` before tier-snapping
- **AND** the next `ApplyChanges` submits the bumped target to Azure

#### Scenario: Recovery converges — bump accumulates until target exceeds actual data
- **WHEN** the actual data stored in the pool is D GB and the formula-computed target starts at T GB (where T < D)
- **THEN** after at most `ceil((D - T) / 50)` retry cycles, the bumped target exceeds D GB
- **AND** Azure accepts the patch
- **AND** the resource resumes normal autoscaling

#### Scenario: Recovery bump resets after a successful apply
- **WHEN** `ApplyChanges` completes without an exception (patch accepted by Azure)
- **AND** `storageRecoveryBumpBytes` is greater than zero
- **THEN** `storageRecoveryBumpBytes` is reset to zero immediately inside `ApplyChanges`
- **AND** an informational log entry is emitted indicating recovery is complete and normal autoscaling resumes

#### Scenario: Recovery bump is not persisted across process restarts
- **WHEN** the autoscaler process restarts during an active recovery cycle
- **THEN** `_storageRecoveryBumpBytes` resets to zero and the recovery cycle restarts from the formula-computed target
- **AND** the pool eventually recovers within the same number of retry cycles as a fresh start

### Requirement: Documentation warns about metric anomaly and stuck-pool risks for SQL elastic pools
The `docs/resources/sql-elastic-pool.md` documentation file SHALL include a dedicated warning section covering: (a) the risk of a transient Azure Monitor metric anomaly causing an unsafe storage scale-down, (b) the recommended protective configuration (`Window`, `ValidValueMin`, and formula pattern), and (c) the automatic recovery mechanism and what operators can observe in logs.

#### Scenario: Operator configuring a new elastic pool finds protective configuration guidance
- **WHEN** an operator reads `docs/resources/sql-elastic-pool.md` before configuring the `MaxDataBytes` scaling rule
- **THEN** the documentation includes a warning section explaining metric anomalies, the required `ValidValueMin` threshold (at least 1 GB), the recommended `Window` (at least 15 min), and the `.Max()` formula pattern

#### Scenario: Operator observing CRITICAL log entries understands the recovery sequence
- **WHEN** an operator sees CRITICAL log entries about `ElasticPoolDecreaseStorageLimitBelowUsage` and incremental bump
- **THEN** the documentation explains what these log entries mean, that automatic recovery is in progress, and under what conditions human intervention is needed
