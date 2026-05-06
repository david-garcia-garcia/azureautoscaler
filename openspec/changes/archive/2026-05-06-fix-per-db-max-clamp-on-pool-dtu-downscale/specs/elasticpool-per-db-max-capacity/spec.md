## ADDED Requirements

### Requirement: PreparePatch finalizes per-database max against target pool DTU
After `PreparePatch` has determined the target pool SKU capacity (`patch.Sku.Capacity`) and copied `patch.PerDatabaseMaxCapacity` from requested state, the autoscaler SHALL set `patch.PerDatabaseMaxCapacity` to `MssqlElasticPoolResourceStateHelper.SnapToNearestPerDbMaxCapacity(patch.Sku, (int)patch.Sku.Capacity, currentPerDbMax)` when both `patch.PerDatabaseMaxCapacity` and `patch.Sku.Capacity` are present, where `currentPerDbMax` is the value of `patch.PerDatabaseMaxCapacity` before this step. This final snap SHALL use the **target** pool DTU, not the live resource’s DTU at refresh time, so the combined ARM patch cannot violate Azure’s per-database maximum for the pool tier (avoiding `ElasticPoolDbDtuMaxAboveLimit`).

#### Scenario: Pool eDTU reduced below prior per-DB max in the same patch
- **WHEN** the live pool has a higher eDTU than the target patch SKU, `SetPerDatabaseMaxCapacity` (or prior logic) left an effective per-DB max that is valid for the old pool size but above the ceiling for `patch.Sku.Capacity`
- **THEN** `PreparePatch` reduces `patch.PerDatabaseMaxCapacity` via `SnapToNearestPerDbMaxCapacity` so it does not exceed the valid per-DB ceiling for `(patch.Sku, patch.Sku.Capacity)` before `ResourcePatchOperation` is returned

#### Scenario: Re-clamp is skipped when per-DB max is absent from patch
- **WHEN** `patch.PerDatabaseMaxCapacity` is null after copying from requested state (fallback in `ApplyChanges` may still use pool capacity)
- **THEN** `PreparePatch` does not attempt `SnapToNearestPerDbMaxCapacity` for per-DB max in this step
