## 1. Extend State DTO

- [x] 1.1 Add `int? PerDatabaseMaxCapacity` property to `MssqlElasticPoolState`

## 2. Extend Helper with Per-DB Capacity Tables

- [x] 2.1 Add `StandardPerDbMaxCapacities` static array `[10, 20, 50, 100, 200, 300, 400, 800, 1200, 1600, 2000, 2500, 3000]` to `MssqlElasticPoolResourceStateHelper`
- [x] 2.2 Add `PremiumPerDbMaxCapacities` static array `[25, 50, 75, 125, 250, 500, 1000, 1750, 4000]` to `MssqlElasticPoolResourceStateHelper`
- [x] 2.3 Add `PremiumPerDbCeiling` dictionary (pool DTU → max per-DB eDTU) encoding: 125→125, 250→250, 500→500, 1000→1000, 1500→1000, 2000→1750, 2500→1750, 3000→1750, 3500→1750, 4000→4000
- [x] 2.4 Add `GetPerDbMaxCapacityValues(SqlSku sku, int poolDtu)` method: for StandardPool return all values ≤ poolDtu from `StandardPerDbMaxCapacities`; for PremiumPool return all values ≤ `PremiumPerDbCeiling[poolDtu]` from `PremiumPerDbMaxCapacities`

## 3. Update Resource State

- [x] 3.1 Add `SetPerDatabaseMaxCapacity(int value)` method to `MssqlElasticPoolResourceState`: snap to nearest value in `GetPerDbMaxCapacityValues(sku, poolDtu)`, clamping to the effective per-DB ceiling for the pool tier
- [x] 3.2 Populate `ExistingMssqlElasticPoolState.PerDatabaseMaxCapacity` from `Resource.Data.PerDatabaseSettings?.MaxCapacity` in `InternalRefreshAsync`
- [x] 3.3 Extend `PreparePatch` `hasChanges` check to include `PerDatabaseMaxCapacity`
- [x] 3.4 Update `ApplyChanges` to use `internalPatch.PerDatabaseMaxCapacity ?? patch.Sku.Capacity` for `PerDatabaseSettings.MaxCapacity`

## 4. New Dimension Class

- [x] 4.1 Create `autoscaler/dimensions/DimensionAzureSqlElasticPoolPerDatabaseMaxCapacity.cs` implementing `IDimension`
- [x] 4.2 Implement `CanApplyDimension`: returns `true` for `ElasticPoolResource` + `Dimension == "PerDatabaseMaxCapacity"`
- [x] 4.3 Implement `GetCurrentDimensionValue`: returns `elasticPool.Data.PerDatabaseSettings?.MaxCapacity?.ToString()`
- [x] 4.4 Implement `GetRequestedDimensionValue`: returns `RequestedMssqlElasticPoolState.PerDatabaseMaxCapacity?.ToString()`
- [x] 4.5 Implement `GetNextDimensionValue` / `GetPreviousDimensionValue` using `MssqlElasticPoolResourceStateHelper.GetPerDbMaxCapacityValues(sku, poolDtu)`
- [x] 4.6 Implement `SetDimensionValue`: parse, call `poolState.SetPerDatabaseMaxCapacity`
- [x] 4.7 Implement `Compare` using integer comparison (same as `DimensionAzureSqlElasticPoolDtu`)

## 5. Register Dimension

- [x] 5.1 Add `new DimensionAzureSqlElasticPoolPerDatabaseMaxCapacity()` to the dimensions list in `Program.cs` alongside the other elastic pool dimensions

## 6. Tests

- [x] 6.1 Create `autoscalertests/DimensionAzureSqlElasticPoolPerDatabaseMaxCapacityTests.cs` mirroring `DimensionAzureSqlElasticPoolDtuTests`
- [x] 6.2 Test `CanApplyDimension` returns `true` for elastic pool + correct dimension name
- [x] 6.3 Test `CanApplyDimension` returns `false` for non-matching dimension name
- [x] 6.4 Test `GetPerDbMaxCapacityValues` for StandardPool 100 eDTU returns `[10, 20, 50, 100]`
- [x] 6.5 Test `GetPerDbMaxCapacityValues` for PremiumPool 1500 eDTU returns values capped at 1000 (not 1500)
- [x] 6.6 Test `SetDimensionValue` snaps up to nearest valid per-DB tier (e.g. 120 → 200 on StandardPool 200 eDTU)
- [x] 6.7 Test `SetDimensionValue` clamps to effective per-DB ceiling (e.g. 9999 on a 1500 eDTU PremiumPool → 1000)
- [x] 6.8 Test fallback: `ApplyChanges` with null `PerDatabaseMaxCapacity` sends `MaxCapacity = Sku.Capacity`
- [x] 6.9 Test `ApplyChanges` with explicit `PerDatabaseMaxCapacity` sends that value
- [x] 6.10 Test `PreparePatch` `HasChanges` is `false` when `PerDatabaseMaxCapacity` is unchanged
- [x] 6.11 Test `PreparePatch` `HasChanges` is `true` when `PerDatabaseMaxCapacity` changes

## 7. Documentation

- [x] 7.1 Update `readme.md` table of supported dimensions for Azure SQL Elastic Pools to include `PerDatabaseMaxCapacity`
- [x] 7.2 In `readme.md` (SQL Elastic Pool section and/or Supported Dimensions section), briefly document: dimension name, that it maps to `PerDatabaseSettings.MaxCapacity`, that valid values follow Azure per-database max eDTU tables (Standard vs Premium; Premium ceiling can be below pool eDTU), and that omitting rules for this dimension keeps the existing apply-time fallback (`MaxCapacity = pool DTU`)
