## Why

The `readme.md` has grown to 1,573 lines, mixing high-level project overview with deep configuration reference and per-resource examples. This makes the file hard to navigate for new users (who want a quick overview) and existing users (who need a specific resource's reference). A `docs/` folder structure is the standard OSS pattern for this scale of documentation.

## What Changes

- `readme.md` is trimmed to ~200 lines: project overview, features, supported resources table, licensing summary, quick-start snippet, and a links table pointing into `docs/`
- A new `docs/` folder is created at the repository root
- Installation content (Docker, Kubernetes, Container Services) moves to `docs/installation.md`
- The full configuration reference (global structure, resource structure, metrics, forecast, scaling rules) moves to `docs/configuration.md`
- Each per-resource example section moves to its own file under `docs/resources/`:
  - `docs/resources/aks-node-pool.md`
  - `docs/resources/sql-elastic-pool.md`
  - `docs/resources/sql-database.md`
  - `docs/resources/postgresql-flexible-server.md`
  - `docs/resources/mysql-flexible-server.md`
  - `docs/resources/azure-files.md`
  - `docs/resources/azure-devops-parallel-jobs.md`
- Custom metrics reference moves to `docs/custom-metrics.md`
- All internal cross-links (anchor links, `#section` references) are updated to point to the new file locations
- The table of contents in `readme.md` is updated to reflect the new structure

## Capabilities

### New Capabilities

- `docs-structure`: A `docs/` folder containing installation, configuration reference, per-resource guides, and custom metrics documentation, linked from a slimmed-down `readme.md`

### Modified Capabilities

*(none — this is a documentation reorganization with no behavior changes)*

## Impact

- `readme.md`: heavily reduced, becomes a project portal
- New files: `docs/installation.md`, `docs/configuration.md`, `docs/custom-metrics.md`, `docs/resources/*.md` (7 files)
- All existing anchor links within the readme must be updated to use file-relative paths
- No code changes; no API changes; no breaking changes to the autoscaler itself
