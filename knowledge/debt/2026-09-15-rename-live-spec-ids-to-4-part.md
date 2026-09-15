# Rename live OpenSpec catalog ids to 4-part slugs

IssueKey: 2026-09-15-sql-query-synthetic-metrics
Size: large
Action: note

## Why this follow-up

Live folders under `openspec/specs/` use kebab-case ids (`docs-structure`, `forecast-feature`, `resource-instance-filter`, …). Librarian Naming requires exactly four underscore parts: `{root}_{domain}_{component}_{spec-name}`. This change added a legal new id (`core_metrics_custom_query`) and did not rename the existing families.

## Why it was not taken

Many dependents and archive folders already cite the kebab ids. Unattended take is only small rows on files this run created. Do not rename existing kebab spec folders without approval.

## Risks

FindSpecHost and map refresh keep treating the live catalog as a bag of vague kebab families. Later deltas fold into those names instead of a 4-part family. Checkers that expect `domains.md` + 4-part folders will fail until archive or a dedicated rename.

## Context

Current: `openspec/specs/docs-structure`, `forecast-feature`, `elastic-pool-storage-metric-resilience`, `resource-instance-filter`, `transient-azure-error-short-disable`, `elastic-pool-stuck-storage-recovery`, `optional-time-window`, `resources-include`, `elasticpool-per-db-max-capacity`, `scale-operation-completion-log`, `forecast-baseline-aggregation`
Proposed: 4-part ids under allowlisted `openspec/specs/domains.md` (create the allowlist at archive unless a checker needs it sooner). This change’s delta stays `core_metrics_custom_query`.
