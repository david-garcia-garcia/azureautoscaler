## Context

The per-database max capacity dimension (`elasticpool-per-db-max-capacity-dimension`) calls `SetPerDatabaseMaxCapacity`, which snaps values using `MssqlElasticPoolResourceStateHelper.SnapToNearestPerDbMaxCapacity(sku, poolDtu, value)` with **`sku` and `poolDtu` read from the live ARM resource** (`Resource.Data.Sku`). Rule evaluation can lower both **pool eDTU** and **per-DB max** in one pass. `PreparePatch` then builds a single `MssqlElasticPoolState` with the new SKU capacity and the requested per-DB max. If the per-DB max was snapped using the **old** (larger) pool DTU, it may still be **above** the valid ceiling for the **new** (smaller) pool DTU. Azure ARM returns HTTP **400** with `ElasticPoolDbDtuMaxAboveLimit` for that payload.

## Goals / Non-Goals

**Goals:**

- Ensure the combined patch always respects per-DB max limits for the **target** pool SKU/capacity before `ApplyChanges` runs.
- Reuse existing snapping logic (`SnapToNearestPerDbMaxCapacity`) so tier tables and Premium ceilings stay consistent.
- Prevent duplicate or conflicting logic in `ApplyChanges` by fixing the patch at the source (`PreparePatch`).

**Non-Goals:**

- Changing how `SetPerDatabaseMaxCapacity` reads live SKU for incremental rule application.
- New Azure API versions or changes to storage / DTU reconciliation beyond what `PreparePatch` already does.

## Decisions

1. **Re-clamp in `PreparePatch` after `patch.Sku.Capacity` is finalized**  
   **Rationale:** `PreparePatch` already adjusts `patch.Sku.Capacity` (e.g. storage-driven `FindClosestDtuThatCanHoldStorage`). The authoritative ceiling for per-DB max in the outbound patch is `(patch.Sku, patch.Sku.Capacity)`, not the pre-patch live resource.  
   **Alternatives considered:** Only fix in `ApplyChanges` — rejected because `HasChanges` and in-memory patch state would still be wrong relative to what Azure accepts; change `SetPerDatabaseMaxCapacity` to accept target pool DTU — larger ripple and harder to reason about when only one dimension updates.

2. **Call `SnapToNearestPerDbMaxCapacity(patch.Sku, (int)patch.Sku.Capacity.Value, (int)patch.PerDatabaseMaxCapacity.Value)`**  
   **Rationale:** One code path for tier snapping and Premium per-DB ceilings; no new validation tables.

3. **Regression test at `PreparePatch` level**  
   **Rationale:** Encodes the failure mode (simultaneous pool DTU downscale + per-DB max) without needing ARM mocks.

## Risks / Trade-offs

- **[Risk]** `RequestedMssqlElasticPoolState.PerDatabaseMaxCapacity` can differ from `patch.PerDatabaseMaxCapacity` after the final snap if the user inspects state mid-cycle.  
  **Mitigation:** Patch and ARM apply use `patch` data; that is the value that must be valid. Documented in spec as intentional finalization in `PreparePatch`.

- **[Risk]** Skipping re-clamp when only one of per-DB max or pool capacity is null could theoretically leave edge cases.  
  **Mitigation:** Re-clamping runs only when both `patch.PerDatabaseMaxCapacity` and `patch.Sku.Capacity` are set, which preserves existing null/fallback behaviour in `ApplyChanges`.

## Migration Plan

No data migration. Deploy as a normal application update. Rollback is reverting the code change; no Azure resource state migration.

## Open Questions

None for this bug fix.
