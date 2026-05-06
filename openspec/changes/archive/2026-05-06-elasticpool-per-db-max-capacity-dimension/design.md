## Context

Azure SQL Elastic Pool resources expose `ElasticPoolPerDatabaseSettings`, which includes `MaxCapacity` (the eDTU cap a single member database may consume) and `MinCapacity`. Currently, `ApplyChanges` always writes `PerDatabaseSettings.MaxCapacity = pool SKU capacity`, coupling the two values together.

The autoscaler already models elastic pool dimensions as distinct `IDimension` implementations (`DimensionAzureSqlElasticPoolDtu`, `DimensionAzureSqlElasticPoolMaxDataBytes`). The same pattern will be applied to per-database max capacity.

**Current state:**
- `MssqlElasticPoolState` has no `PerDatabaseMaxCapacity` field.
- `ApplyChanges` synthesizes the value at patch time; it is never read back from Azure.
- Operators have no YAML surface to control this setting.

**Constraints:**
- `PerDatabaseSettings.MaxCapacity` must be a value from a **per-database-specific** set that is distinct from the pool-level capacity table. The ARM API rejects values not in the allowed set.
- **Standard** per-DB max eDTU allowed values (filter to ≤ pool eDTU): `[10, 20, 50, 100, 200, 300, 400, 800, 1200, 1600, 2000, 2500, 3000]`
- **Premium** per-DB max eDTU has a non-trivial cap that is NOT simply the pool eDTU. The valid set is `[25, 50, 75, 125, 250, 500, 1000, 1750, 4000]` filtered by a pool-size-dependent ceiling: pools ≤1000 eDTU cap at pool size; 1500 eDTU pool caps at 1000; 2000–3500 eDTU pools cap at 1750; 4000 eDTU pool caps at 4000.
- The existing default behaviour (MaxCapacity = pool capacity) must be preserved when no `PerDatabaseMaxCapacity` rule is configured.

## Goals / Non-Goals

**Goals:**
- Expose `PerDatabaseSettings.MaxCapacity` as a first-class dimension named `PerDatabaseMaxCapacity`.
- Allow operators to write independent scaling rules for this dimension.
- Preserve the existing fallback (MaxCapacity = pool DTU) when the dimension is absent.
- Follow the exact patterns already established by `DimensionAzureSqlElasticPoolDtu`.

**Non-Goals:**
- `PerDatabaseSettings.MinCapacity` is not in scope.
- vCore elastic pool per-database settings are not in scope (only DTU tiers: StandardPool, PremiumPool).
- No config schema changes beyond the new `Dimension` name string.

## Decisions

### D1 — Extend `MssqlElasticPoolState` with `PerDatabaseMaxCapacity`

`MssqlElasticPoolState` gains `int? PerDatabaseMaxCapacity`. Nullable so that "not set by any rule" can be distinguished from "set to a value".

**Alternatives considered:**
- A separate DTO — rejected; the existing state DTO is already the right place and all other per-patch fields live there.

### D2 — Read live value in `InternalRefreshAsync`

`MssqlElasticPoolResourceState.InternalRefreshAsync` populates `ExistingMssqlElasticPoolState.PerDatabaseMaxCapacity` from `Resource.Data.PerDatabaseSettings?.MaxCapacity`. This lets rules compare current vs. requested and is consistent with how `MaxSizeBytes` and `Sku` are loaded.

### D3 — Fallback in `ApplyChanges`

```
patch.PerDatabaseSettings.MaxCapacity =
    internalPatch.PerDatabaseMaxCapacity ?? patch.Sku.Capacity;
```

When no dimension rule has set `RequestedMssqlElasticPoolState.PerDatabaseMaxCapacity`, the value is `null` and the existing behaviour is preserved. This is a zero-regression approach.

### D4 — Clamp requested value to valid per-DB table and effective pool ceiling

`SetPerDatabaseMaxCapacity` snaps to the nearest value in `GetPerDbMaxCapacityValues(sku, poolDtu)` that is ≥ the requested value. For Premium, the effective ceiling from the table above (not raw pool DTU) is used for clamping, since e.g. a 1500 eDTU pool only allows up to 1000 per-DB.

**Alternatives considered:**
- Clamp only at apply time — possible but moves failure detection late; clamping at set time surfaces the constraint earlier in logs.

### D5 — New per-database-specific lookup tables in the helper

The per-database max DTU allowed values differ from the pool-level capacity table. Two new static arrays are added to `MssqlElasticPoolResourceStateHelper`:

```
StandardPerDbMaxCapacities = [10, 20, 50, 100, 200, 300, 400, 800, 1200, 1600, 2000, 2500, 3000]
PremiumPerDbMaxCapacities  = [25, 50, 75, 125, 250, 500, 1000, 1750, 4000]
```

A new helper method `GetPerDbMaxCapacityValues(SqlSku sku, int poolDtu)` returns the slice of the appropriate array that is valid for the current pool size. For Standard, all values ≤ `poolDtu` are valid. For Premium, an additional pool-size ceiling table is consulted:

| Pool eDTU | Max per-DB eDTU |
|-----------|----------------|
| 125       | 125            |
| 250       | 250            |
| 500       | 500            |
| 1000      | 1000           |
| 1500      | 1000           |
| 2000      | 1750           |
| 2500      | 1750           |
| 3000      | 1750           |
| 3500      | 1750           |
| 4000      | 4000           |

`GetNextDimensionValue` / `GetPreviousDimensionValue` navigate this per-DB array (not `GetCapacityValues`).

**Alternatives considered:**
- Reuse `GetCapacityValues` — rejected; the per-DB value set is different (e.g., Standard per-DB starts at 10 while pool starts at 50; Premium per-DB includes 75, 1750 which are not in the pool table).

### D6 — Include `hasChanges` check for `PerDatabaseMaxCapacity`

`PreparePatch` already computes `hasChanges`. The check is extended:

```
bool hasChanges = ...
    || (patch.PerDatabaseMaxCapacity != null
        && patch.PerDatabaseMaxCapacity != ExistingMssqlElasticPoolState.PerDatabaseMaxCapacity);
```

## Risks / Trade-offs

- **PerDatabaseMaxCapacity > effective pool ceiling after pool scale-down** → Mitigation: `SetPerDatabaseMaxCapacity` uses `GetPerDbMaxCapacityValues` which consults the pool-size ceiling table, not raw pool DTU; the ARM API also enforces this.
- **Premium pool ceiling is not equal to pool DTU** (e.g., 1500 eDTU pool caps per-DB at 1000) → Mitigation: a dedicated `PremiumPerDbCeiling` lookup table in the helper encodes the exact ceiling per pool tier; the fallback in `ApplyChanges` is bounded by the same ceiling.
- **ARM may set a default PerDatabaseMaxCapacity on pool create that differs from pool capacity** → Mitigation: `GetCurrentDimensionValue` reads the live ARM value so rules always compare against reality.
- **Tests gap** → New unit tests mirror `DimensionAzureSqlElasticPoolDtuTests`; mocked `ElasticPoolData` populates `PerDatabaseSettings`.
