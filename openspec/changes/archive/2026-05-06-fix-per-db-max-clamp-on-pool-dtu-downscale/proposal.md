## Why

When pool eDTU and per-database max capacity are scaled down in the same cycle, the per-DB value was validated against the **live** pool DTU while the ARM patch carried a **lower** target pool DTU. Azure then rejects the combined update with `ElasticPoolDbDtuMaxAboveLimit` (HTTP 400). This change documents the fix that re-clamps per-DB max against the **target** pool DTU in `PreparePatch` before the patch is sent.

## What Changes

- **PreparePatch** (`MssqlElasticPoolResourceState`): after `patch.PerDatabaseMaxCapacity` is resolved, re-snap it with `MssqlElasticPoolResourceStateHelper.SnapToNearestPerDbMaxCapacity(patch.Sku, targetPoolDtu, perDbMax)` so the outbound patch never exceeds the ceiling implied by `patch.Sku.Capacity`.
- **Regression test** `PreparePatch_WhenPoolDtuReducedBelowExistingPerDbMax_ShouldClampPerDbMaxToNewPoolDtu` in `MssqlElasticPoolResourceStateTests` to lock the behaviour.

## Capabilities

### New Capabilities

_(None — behaviour extends the existing elastic pool per-DB max capability.)_

### Modified Capabilities

- `elasticpool-per-db-max-capacity`: Add a normative requirement that `PreparePatch` finalizes `PerDatabaseMaxCapacity` against the **target** pool DTU when both dimensions change together, preventing invalid combined ARM patches.

## Impact

- **Code**: `autoscaler/resources/MssqlElasticPool/MssqlElasticPoolResourceState.cs` (`PreparePatch`).
- **Tests**: `autoscalertests/MssqlElasticPoolResourceStateTests.cs`.
- **Azure**: Same ARM API; fewer failed PATCH operations when downscaling both dimensions.
- **Related change**: Follow-on to `elasticpool-per-db-max-capacity-dimension`.
