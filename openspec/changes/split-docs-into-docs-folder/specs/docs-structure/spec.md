## ADDED Requirements

### Requirement: Docs folder exists with expected files
The repository SHALL contain a `docs/` folder at the root with the following files:
- `docs/installation.md`
- `docs/configuration.md`
- `docs/custom-metrics.md`
- `docs/resources/aks-node-pool.md`
- `docs/resources/sql-elastic-pool.md`
- `docs/resources/sql-database.md`
- `docs/resources/postgresql-flexible-server.md`
- `docs/resources/mysql-flexible-server.md`
- `docs/resources/azure-files.md`
- `docs/resources/azure-devops-parallel-jobs.md`

#### Scenario: All expected doc files are present
- **WHEN** the repository is checked out
- **THEN** all files listed above SHALL exist

### Requirement: README is a project portal
`readme.md` SHALL contain: project title, brief description, features list, supported resource types table, licensing summary, quick-start snippet, and a navigation section with links to all `docs/` files. It SHALL NOT contain the full installation guide, configuration reference, or per-resource examples.

#### Scenario: README is under 300 lines
- **WHEN** `readme.md` is read
- **THEN** it SHALL be no more than 300 lines long

#### Scenario: README links to docs files
- **WHEN** `readme.md` is read
- **THEN** it SHALL contain relative links to `docs/installation.md`, `docs/configuration.md`, and each `docs/resources/*.md` file

### Requirement: No content is lost
All documentation content that existed in the original `readme.md` SHALL be preserved in one of the new `docs/` files.

#### Scenario: Installation content is in docs/installation.md
- **WHEN** `docs/installation.md` is read
- **THEN** it SHALL contain all Docker, Kubernetes, and Container Services installation instructions

#### Scenario: Configuration reference is in docs/configuration.md
- **WHEN** `docs/configuration.md` is read
- **THEN** it SHALL contain the full configuration reference: global structure, resource structure, metrics, forecast, and scaling rules sections

#### Scenario: Each resource has its own file
- **WHEN** a `docs/resources/<resource>.md` file is read
- **THEN** it SHALL contain all configuration examples and notes for that resource type that were previously in `readme.md`

### Requirement: Internal cross-links are valid
All cross-links within `docs/` files and from `readme.md` to `docs/` files SHALL use relative paths and SHALL resolve to existing files and headings.

#### Scenario: Links between docs files use relative paths
- **WHEN** a link from one doc file references another doc file
- **THEN** the link SHALL use a relative path (e.g., `../configuration.md#section`) not an absolute URL
