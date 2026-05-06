# Elastic pool per-DB max capacity

### Requirement: PerDatabaseMaxCapacity dimension for Elastic Pool
The autoscaler SHALL expose `ElasticPoolPerDatabaseSettings.MaxCapacity` as a configurable scaling dimension named `PerDatabaseMaxCapacity` for `Microsoft.Sql/servers/elasticPools` resources.

#### Scenario: Dimension activates for elastic pool rules with PerDatabaseMaxCapacity
- **WHEN** a scaling rule targets an `ElasticPoolResource` and `rule.Dimension == "PerDatabaseMaxCapacity"`
- **THEN** `DimensionAzureSqlElasticPoolPerDatabaseMaxCapacity.CanApplyDimension` returns `true`

#### Scenario: Dimension is ignored for non-elastic-pool resources
- **WHEN** a scaling rule targets a resource that is not an `ElasticPoolResource`
- **THEN** `CanApplyDimension` returns `false`

---

### Requirement: Live per-database max capacity is read from Azure on refresh
The autoscaler SHALL populate `ExistingMssqlElasticPoolState.PerDatabaseMaxCapacity` from `Resource.Data.PerDatabaseSettings.MaxCapacity` during `InternalRefreshAsync`.

#### Scenario: Existing value is captured after refresh
- **WHEN** the pool's live `PerDatabaseSettings.MaxCapacity` is `100` in Azure
- **THEN** `ExistingMssqlElasticPoolState.PerDatabaseMaxCapacity` equals `100` after refresh

---

### Requirement: Requested per-database max capacity is applied at patch time
`ApplyChanges` SHALL use `RequestedMssqlElasticPoolState.PerDatabaseMaxCapacity` when it is set, and SHALL fall back to `patch.Sku.Capacity` when it is `null`.

#### Scenario: Rule sets per-database max capacity
- **WHEN** `RequestedMssqlElasticPoolState.PerDatabaseMaxCapacity` is `100`
- **THEN** the ARM patch sends `PerDatabaseSettings.MaxCapacity = 100`

#### Scenario: No rule sets per-database max capacity (fallback preserved)
- **WHEN** `RequestedMssqlElasticPoolState.PerDatabaseMaxCapacity` is `null`
- **THEN** the ARM patch sends `PerDatabaseSettings.MaxCapacity = patch.Sku.Capacity` (existing default behaviour)

---

### Requirement: Requested value is clamped to valid per-DB DTU tier and effective pool ceiling
`SetPerDatabaseMaxCapacity` SHALL snap the requested value to the nearest valid value in `GetPerDbMaxCapacityValues(sku, poolDtu)` that is ≥ the requested value, and SHALL not exceed the effective per-DB ceiling for the pool tier (which for Premium may be lower than the raw pool DTU).

#### Scenario: Value is snapped up to nearest valid per-DB tier on StandardPool
- **WHEN** the operator requests `PerDatabaseMaxCapacity = 120` on a 200 eDTU StandardPool (valid per-DB values: 10, 20, 50, 100, 200)
- **THEN** `RequestedMssqlElasticPoolState.PerDatabaseMaxCapacity` is set to `200`

#### Scenario: Value is clamped to effective per-DB ceiling for Premium pool
- **WHEN** the operator requests `PerDatabaseMaxCapacity = 9999` on a 1500 eDTU PremiumPool
- **THEN** `RequestedMssqlElasticPoolState.PerDatabaseMaxCapacity` is clamped to `1000` (the Azure-defined per-DB ceiling for 1500 eDTU Premium pools)

---

### Requirement: Step-up and step-down navigate valid per-DB DTU tiers
`GetNextDimensionValue` and `GetPreviousDimensionValue` SHALL return the adjacent value in `MssqlElasticPoolResourceStateHelper.GetPerDbMaxCapacityValues(sku, poolDtu)`, which uses the per-database-specific value tables (not the pool-level capacity table).

#### Scenario: Step up from 100 on a 200 eDTU StandardPool
- **WHEN** current `PerDatabaseMaxCapacity` is `100` on a 200 eDTU StandardPool (valid per-DB values: 10, 20, 50, 100, 200) and `GetNextDimensionValue` is called
- **THEN** the returned value is `200`

#### Scenario: Step down from 100 on a StandardPool
- **WHEN** current `PerDatabaseMaxCapacity` is `100` on a StandardPool and `GetPreviousDimensionValue` is called
- **THEN** the returned value is `50`

#### Scenario: Step down at minimum per-DB tier returns minimum
- **WHEN** current `PerDatabaseMaxCapacity` is `10` (lowest StandardPool per-DB tier) and `GetPreviousDimensionValue` is called
- **THEN** the returned value is `10`

#### Scenario: Valid per-DB values for a 100 eDTU StandardPool are capped at pool size
- **WHEN** `GetPerDbMaxCapacityValues` is called with a 100 eDTU StandardPool
- **THEN** the returned values are `[10, 20, 50, 100]` (not 200 or higher)

---

### Requirement: hasChanges includes per-database max capacity
`PreparePatch` SHALL include `PerDatabaseMaxCapacity` in the `hasChanges` check so that a patch is only sent when the value actually differs from the existing state.

#### Scenario: No patch when PerDatabaseMaxCapacity is unchanged
- **WHEN** the requested `PerDatabaseMaxCapacity` equals the existing `PerDatabaseMaxCapacity` and no other dimension changed
- **THEN** `ResourcePatchOperation.HasChanges` is `false`

#### Scenario: Patch sent when PerDatabaseMaxCapacity changes
- **WHEN** the requested `PerDatabaseMaxCapacity` differs from the existing value
- **THEN** `ResourcePatchOperation.HasChanges` is `true`

---

### Requirement: PreparePatch finalizes per-database max against target pool DTU
After `PreparePatch` has determined the target pool SKU capacity (`patch.Sku.Capacity`) and copied `patch.PerDatabaseMaxCapacity` from requested state, the autoscaler SHALL set `patch.PerDatabaseMaxCapacity` to `MssqlElasticPoolResourceStateHelper.SnapToNearestPerDbMaxCapacity(patch.Sku, (int)patch.Sku.Capacity, currentPerDbMax)` when both `patch.PerDatabaseMaxCapacity` and `patch.Sku.Capacity` are present, where `currentPerDbMax` is the value of `patch.PerDatabaseMaxCapacity` before this step. This final snap SHALL use the **target** pool DTU, not the live resource’s DTU at refresh time, so the combined ARM patch cannot violate Azure’s per-database maximum for the pool tier (avoiding `ElasticPoolDbDtuMaxAboveLimit`).

#### Scenario: Pool eDTU reduced below prior per-DB max in the same patch
- **WHEN** the live pool has a higher eDTU than the target patch SKU, `SetPerDatabaseMaxCapacity` (or prior logic) left an effective per-DB max that is valid for the old pool size but above the ceiling for `patch.Sku.Capacity`
- **THEN** `PreparePatch` reduces `patch.PerDatabaseMaxCapacity` via `SnapToNearestPerDbMaxCapacity` so it does not exceed the valid per-DB ceiling for `(patch.Sku, patch.Sku.Capacity)` before `ResourcePatchOperation` is returned

#### Scenario: Re-clamp is skipped when per-DB max is absent from patch
- **WHEN** `patch.PerDatabaseMaxCapacity` is null after copying from requested state (fallback in `ApplyChanges` may still use pool capacity)
- **THEN** `PreparePatch` does not attempt `SnapToNearestPerDbMaxCapacity` for per-DB max in this step
