# Explore

## Concepts
- OpenSpec **live catalog** lives at `openspec/specs/{spec-id}/spec.md`. Librarian grammar: exactly four underscore-separated parts; root and domain must appear in `openspec/specs/domains.md`.
- **Rename** here is catalog hygiene only: move folders, refresh map, extend allowlist. Archived changes keep historical kebab ids in their own `specs/` subtrees and proposal bullets.
- `core_metrics_custom_query` is the reference legal id already on `1.x`.

## Decisions
- **Id map** (old kebab folder → new live spec id):
  - `docs-structure` → `std_docs_project_structure`
  - `forecast-feature` → `core_forecast_engine_feature`
  - `forecast-baseline-aggregation` → `core_forecast_baseline_aggregation`
  - `resource-instance-filter` → `core_resources_discovery_instance-filter`
  - `resources-include` → `core_config_resources_include`
  - `optional-time-window` → `core_config_scaling_optional-time-window`
  - `scale-operation-completion-log` → `core_scaling_operation_completion-log`
  - `elasticpool-per-db-max-capacity` → `core_resources_elasticpool_per-db-max-capacity`
  - `elastic-pool-storage-metric-resilience` → `core_resources_elasticpool_storage-metric-resilience`
  - `elastic-pool-stuck-storage-recovery` → `core_resources_elasticpool_stuck-storage-recovery`
  - `transient-azure-error-short-disable` → `core_scaling_policy_transient-azure-short-disable`
- **domains.md** additions: under `core` add `forecast`, `resources`, `config`, `scaling`; under `std` add `docs`.
- **Archive citations**: leave archived change folder and proposal kebab labels unchanged; only update references that point at live `openspec/specs/<kebab>/` paths (none found outside debt and foreign devstate).
- **Debt**: delete `knowledge/debt/2026-09-15-rename-live-spec-ids-to-4-part.md` when renames land.

## Open questions
- Q: Should archived `openspec/changes/archive/*/specs/<kebab>/` delta folders be renamed too?
  Rank: bounded asked — only live catalog in requirement; archive skill keeps historical ids in archived changes
  Decision: assumed — rename live catalog only; archived deltas stay as historical artifacts.
  By: explore
