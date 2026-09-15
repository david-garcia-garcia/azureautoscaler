# Requirement
IssueKey: 2026-09-15-rename-live-spec-ids-to-4-part

## Problem
Live OpenSpec catalog folders use illegal kebab-case ids. Naming requires `{root}_{domain}_{component}_{spec-name}` (four underscore parts). Checkers and map generation expect that shape; kebab families block consistent librarian workflow.

## Current (code)
- Eleven kebab folders under `openspec/specs/` (`docs-structure`, `forecast-feature`, …) — each contains `spec.md` (`openspec/specs/*/spec.md`).
- Legal folder `openspec/specs/core_metrics_custom_query/` already exists (`openspec/specs/core_metrics_custom_query/spec.md`).
- Allowlist `openspec/specs/domains.md` lists only `core` / `metrics`.
- Generated map `openspec/specs/map.md` lists only `core` / `metrics` / `custom`.
- Debt tracked at `knowledge/debt/2026-09-15-rename-live-spec-ids-to-4-part.md`.
- Archived change proposals cite kebab ids as historical labels (`openspec/changes/archive/*/proposal.md`); live path references are rare (e.g. other run `devstate/.../specs.md` — out of scope to edit).

## Desired
Rename the eleven kebab live folders to approved 4-part ids (map published in explore/propose). Extend `domains.md`. Regenerate `map.md`. Remove the debt file. Leave `core_metrics_custom_query` unchanged. Do not rewrite archived change folder names or historical proposal bullets unless required by archive skill.

## Affected
- `openspec/specs/` live catalog folders and `domains.md`, `map.md`
- `knowledge/debt/2026-09-15-rename-live-spec-ids-to-4-part.md` (delete)
- Any in-repo paths pointing at old live folder names (grep-driven)

## Out of scope
- Branch `2026-09-15-sql-query-synthetic-metrics`, PR 44, other runs' `devstate/`
- Renaming archived change directories under `openspec/changes/archive/`
- Product runtime code behavior

## Unknowns
- Exact 4-part id per kebab family (decide in explore/propose from spec purpose and domains allowlist).

## Tensions
- Human approved taking a debt item marked `large`; workflow normally notes large renames — explicit override to implement in this PR.
