## Context

`readme.md` is currently 1,573 lines and serves multiple audiences simultaneously: new users evaluating the project, operators installing it, and engineers configuring specific resources. As the number of supported resource types grows, the file grows unboundedly. The standard OSS pattern for this scale is a `docs/` folder at the repository root, with each concern in its own file. GitHub renders `.md` files in `docs/` with full navigation support.

## Goals / Non-Goals

**Goals:**
- Reduce `readme.md` to a project portal (~200 lines)
- Move all installation, configuration reference, and per-resource content to `docs/`
- Preserve all existing content (no information is deleted)
- Maintain all cross-links so no existing anchors break from within the docs themselves
- Keep docs co-located with code in the repository (no external doc site needed)

**Non-Goals:**
- Setting up a doc site (MkDocs, Docusaurus, GitHub Pages) — out of scope
- Changing the content of any documentation section (rewriting, updating, or improving it)
- Restructuring the configuration reference internally
- Any code changes

## Decisions

**D1: Use a flat `docs/` folder with a `resources/` subfolder**

Options considered:
- Flat `docs/` (all files at same level) — simple, but `docs/aks-node-pool.md` etc. clutters the folder as more resources are added
- `docs/` with `resources/` subfolder — groups per-resource guides together, mirrors the codebase's `autoscaler/resources/` structure
- Fully nested by concern (e.g. `docs/reference/configuration/metrics.md`) — over-engineered for current scale

Decision: `docs/` with a `resources/` subfolder. Scales cleanly as new resource types are added.

**D2: Keep `readme.md` as the entry point, not `docs/index.md`**

GitHub renders `README.md` as the repository landing page. Moving all content to `docs/index.md` would break the default GitHub UX. README stays as the portal.

**D3: Do not split the configuration reference further**

The configuration reference (global structure, resource structure, metrics, forecast, scaling rules) is ~640 lines but is a single coherent reference. Splitting it into multiple files (e.g., `docs/configuration/metrics.md`) would fragment navigation for users who scan it top-to-bottom. It goes into a single `docs/configuration.md`.

**D4: Cross-links use relative file paths**

Internal links will use `[text](../configuration.md#section)` style (relative paths with anchors) so they work both in GitHub and in any future doc site. Absolute URLs (e.g., `https://github.com/...`) are not used for internal links.

## Risks / Trade-offs

- **Broken anchor links** → Mitigation: all `#anchor` references in the readme and docs files are reviewed and updated during the task. GitHub heading IDs are predictable (lowercased, spaces to hyphens).
- **Table of contents in readme.md becomes stale** → Mitigation: the new README ToC links directly to doc files, not anchors within README, so it can't drift silently.
- **Discoverability regression** — users who have bookmarked a `readme.md#section` anchor will land on the right file but at the top, not at the section. This is a minor, one-time disruption.

## Migration Plan

1. Create `docs/` and `docs/resources/` directories
2. Extract each section from `readme.md` into its target file, preserving content exactly
3. Update cross-links within each new file to use relative paths
4. Rewrite `readme.md` to be a portal: overview, features table, supported resources table, quick-start, and a links section pointing to `docs/`
5. Verify all links render correctly on GitHub (manual spot-check)
6. No rollback strategy needed — this is a pure file reorganization; git history preserves everything
