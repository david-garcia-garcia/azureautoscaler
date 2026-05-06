## 1. Create folder structure

- [x] 1.1 Create `docs/` directory at the repository root
- [x] 1.2 Create `docs/resources/` subdirectory

## 2. Extract installation docs

- [x] 2.1 Create `docs/installation.md` with the full Installation section from `readme.md` (Docker, Kubernetes/Terraform, Container Services)
- [x] 2.2 Update any internal cross-links within `docs/installation.md` to use relative paths (e.g., links to configuration sections)

## 3. Extract configuration reference

- [x] 3.1 Create `docs/configuration.md` with the full configuration reference from `readme.md`: The configuration file, Configuration lifecycle, Global structure, Resource-Specific Logging, Resource structure, Scaling configurations, Resource Expansion, Resource Filter, Resource Tags, Metrics, Using the Default Property in Rules, Metric Validation, Forecast Metrics and ForecastMode, Scaling Rules, Scaling Rule Strategy Fixed, Scaling Rule Strategy Autoadjust
- [x] 3.2 Update any internal cross-links within `docs/configuration.md` to use relative paths

## 4. Extract custom metrics docs

- [x] 4.1 Create `docs/custom-metrics.md` with the Custom Metrics section from `readme.md` (CustomMetrics configuration options, DataExpression)
- [x] 4.2 Update any internal cross-links within `docs/custom-metrics.md` to use relative paths

## 5. Extract per-resource docs

- [x] 5.1 Create `docs/resources/aks-node-pool.md` with the AKS Node Pool section
- [x] 5.2 Create `docs/resources/sql-elastic-pool.md` with the SQL Elastic Pool section (including Per-database max eDTU subsection)
- [x] 5.3 Create `docs/resources/sql-database.md` with the SQL Database section (DTU and MaxDataBytes examples)
- [x] 5.4 Create `docs/resources/postgresql-flexible-server.md` with the PostgreSQL Flexible Server section
- [x] 5.5 Create `docs/resources/mysql-flexible-server.md` with the MySQL Flexible Server section
- [x] 5.6 Create `docs/resources/azure-files.md` with the Azure Files section
- [x] 5.7 Create `docs/resources/azure-devops-parallel-jobs.md` with the Azure DevOps Parallel Jobs section (Prerequisites, Firewall Requirements, Resource ID Format, Supported Dimensions, Custom Metrics, Example Configuration)
- [x] 5.8 Update any internal cross-links within each resource file to use relative paths (e.g., links to `../configuration.md#metrics`)

## 6. Rewrite readme.md as a portal

- [x] 6.1 Rewrite `readme.md` to contain only: project title, brief description, Why Azure Autoscaler, Features list, Supported Resource Types table, Licensing section, a Quick Start snippet (the minimal Docker example), and a Documentation navigation section
- [x] 6.2 Add a Documentation section to `readme.md` with a table or list linking to `docs/installation.md`, `docs/configuration.md`, `docs/custom-metrics.md`, and each `docs/resources/*.md` file
- [x] 6.3 Verify `readme.md` is under 300 lines

## 7. Verify links

- [x] 7.1 Spot-check all relative links in `docs/` files resolve to existing files and headings
- [x] 7.2 Verify `readme.md` links to all new `docs/` files
