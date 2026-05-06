## Why

`ElasticPoolPerDatabaseSettings.MaxCapacity` — the per-database eDTU/vCore cap that Azure enforces per tenant database inside a pool — is currently always set equal to the pool's total capacity on every patch. This gives the operator no control over the per-database cap, which means aggressive individual databases can starve the pool and operators cannot enforce SLAs at the per-tenant level without managing the cap outside the autoscaler.

## What Changes

- Introduce a new dimension `PerDatabaseMaxCapacity` for `MssqlElasticPool` resources that allows rules to read and set `ElasticPoolPerDatabaseSettings.MaxCapacity` independently of the pool DTU.
- `MssqlElasticPoolState` gains an optional `PerDatabaseMaxCapacity` field.
- `MssqlElasticPoolResourceState` reads the live `MaxCapacity` from Azure on refresh and writes the requested value into the ARM patch; when no dimension rule has set it, the existing default (copy pool capacity) is preserved.
- `ApplyChanges` uses `RequestedMssqlElasticPoolState.PerDatabaseMaxCapacity` if set; otherwise falls back to `patch.Sku.Capacity` (current behaviour, no regression).
- A new `DimensionAzureSqlElasticPoolPerDatabaseMaxCapacity` class is registered in `Program.cs`.

## Capabilities

### New Capabilities

- `elasticpool-per-db-max-capacity`: Dimension that exposes `PerDatabaseSettings.MaxCapacity` as a first-class autoscaler dimension for Azure SQL Elastic Pool resources, allowing operators to configure scaling rules for the per-database cap independently of the pool-level capacity.

### Modified Capabilities

*(none — existing behaviour is preserved as the default fallback)*

## Impact

- **Code**: `MssqlElasticPool/Dto/MssqlElasticPoolState.cs`, `MssqlElasticPool/MssqlElasticPoolResourceState.cs`, `dimensions/DimensionAzureSqlElasticPoolPerDatabaseMaxCapacity.cs` (new), `Program.cs`
- **Config**: New `Dimension: PerDatabaseMaxCapacity` available in scaling rules for elastic pool resources.
- **Tests**: New unit tests in `autoscalertests/` following the `DimensionAzureSqlElasticPoolDtuTests` pattern.
- **No breaking changes** — existing rules without `PerDatabaseMaxCapacity` keep the current fallback.
