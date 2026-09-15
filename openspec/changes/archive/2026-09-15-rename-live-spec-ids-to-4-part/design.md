# Design

## Approach
Use `git mv` for each live folder under `openspec/specs/` so history is preserved. Do not edit requirement bodies inside `spec.md` except if headings still reference the old kebab id in the title line (update H1 to match the unit name where present).

## domains.md
Add domains in the same commit as the first folder under that pair:
- `core`: `forecast`, `resources`, `config`, `scaling` (retain `metrics`)
- `std`: `docs`

## Validation
After all moves, run OpenDev MCP `validate_spec_map` with `write: true`, verify mode, then `validate_artifact_names` with `repoRoot` set to this worktree.

## References
Grep for `openspec/specs/<old-kebab>` outside `openspec/changes/archive/` and update to new paths. Do not edit other OpenDev run folders under `devstate/`.

## Debt
Remove `knowledge/debt/2026-09-15-rename-live-spec-ids-to-4-part.md` and mark the follow-up taken on this run's `issues.md`.
