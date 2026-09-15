# Rename live OpenSpec catalog ids to 4-part slugs

Human explicitly approved TAKING this previously-noted large debt (rename existing kebab live spec folders) and landing it in its own PR.

Live folders under `openspec/specs/` use kebab-case ids. Librarian Naming requires exactly four underscore parts. `core_metrics_custom_query` is already legal and must not be renamed.

Kebab ids to rename: docs-structure, forecast-feature, elastic-pool-storage-metric-resilience, resource-instance-filter, transient-azure-error-short-disable, elastic-pool-stuck-storage-recovery, optional-time-window, resources-include, elasticpool-per-db-max-capacity, scale-operation-completion-log, forecast-baseline-aggregation.

Update `openspec/specs/map.md`, extend `openspec/specs/domains.md`, in-tree references to old live folder paths. Delete `knowledge/debt/2026-09-15-rename-live-spec-ids-to-4-part.md` when done. Do not edit another run's devstate or PR 44 branch.
