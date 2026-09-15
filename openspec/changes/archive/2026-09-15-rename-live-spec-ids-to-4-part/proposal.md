# Rename live OpenSpec catalog ids to 4-part slugs

## Why
Kebab-case live folders under `openspec/specs/` violate librarian naming (four underscore parts). Map generation and artifact checkers only see legal families. Human approved landing this debt in a dedicated PR.

## What changes
Rename eleven live catalog folders (content unchanged). Extend `openspec/specs/domains.md`. Regenerate `openspec/specs/map.md`. Delete `knowledge/debt/2026-09-15-rename-live-spec-ids-to-4-part.md`.

## Specs (live catalog — folder renames)
| Old id | New id |
|--------|--------|
| docs-structure | std_docs_project_structure |
| forecast-feature | core_forecast_engine_feature |
| forecast-baseline-aggregation | core_forecast_baseline_aggregation |
| resource-instance-filter | core_resources_discovery_instance-filter |
| resources-include | core_config_resources_include |
| optional-time-window | core_config_scaling_optional-time-window |
| scale-operation-completion-log | core_scaling_operation_completion-log |
| elasticpool-per-db-max-capacity | core_resources_elasticpool_per-db-max-capacity |
| elastic-pool-storage-metric-resilience | core_resources_elasticpool_storage-metric-resilience |
| elastic-pool-stuck-storage-recovery | core_resources_elasticpool_stuck-storage-recovery |
| transient-azure-error-short-disable | core_scaling_policy_transient-azure-short-disable |

**Unchanged:** `core_metrics_custom_query`

## Out of scope
Archived change folders and historical proposal bullets keep kebab ids. No product runtime changes.
