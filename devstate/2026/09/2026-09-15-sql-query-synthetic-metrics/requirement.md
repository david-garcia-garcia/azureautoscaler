# Requirement
IssueKey: 2026-09-15-sql-query-synthetic-metrics

## Problem
Operators want Azure Monitor (portal-visible) metrics on SQL resources that match what the Azure portal shows for the primary database—DTU, CPU, memory, and data I/O—especially where built-in Azure Monitor metrics are missing, delayed, or differ on read replicas. The autoscaler should be able to derive those values via configured queries and publish them as custom/synthetic metrics.

## Current (code)
- Scaling metrics for Azure Monitor use `MetricsQueryClient` in `autoscaler/metrics/AzureMonitorMetricsGatherer.cs`; names starting with `custom_` call `ResourceState.CustomMetric()` (`autoscaler/resourcemanagement/ResourceState.cs`).
- `CustomMetric()` is implemented for Azure DevOps only (`autoscaler/resources/AzureDevops/AzureDevOpsParallelJobsResourceState.cs`); PostgreSQL and MySQL override it but throw `NotImplementedException` for every name (`autoscaler/resources/PostgreSqlFlexibleServer/PostgreSqlFlexibleServerResourceState.cs`, `autoscaler/resources/MySqlFlexibleServer/MySqlFlexibleServerResourceState.cs`). Azure SQL Database and Elastic Pool use the base `NotImplementedException` (`autoscaler/resourcemanagement/ResourceState.cs`).
- Pushed custom metrics use `CustomMetrics` + required `DataExpression` compiled to `DataExpressionDelegate` (`autoscaler/configuration/CustomMetricConfig.cs`, `autoscaler/metrics/CustomMetricsPusher.cs`); no `Query` or SQL field on config.
- SQL flexible servers can push metrics via `DataExpression` when `BuildCustomMetricDataContextAsync` succeeds (`docs/custom-metrics.md`); SQL DB / elastic pool docs focus on native Monitor metric names in scaling rules (`docs/resources/sql-database.md`, `docs/resources/sql-elastic-pool.md`).
- No code or docs reference “synthetic” metrics or QUERY-driven metric collection (`not found` under repo grep for synthetic; QUERY not in `autoscaler/configuration/`).

## Desired
- For PostgreSQL Flexible Server, MySQL Flexible Server, Azure SQL Database, and Azure SQL Elastic Pool: support defining custom synthetic metrics whose values come from a **QUERY** (ticket wording), not only from `DataExpression` or hard-coded `custom_*` names.
- Published metrics should mirror portal-relevant signals on the **main** database: DTU, CPU, memory, data I/O; explicitly **not** log I/O on read-only replicas (ticket).

## Affected
- `autoscaler/configuration/` (metric / custom metric config surface).
- `autoscaler/metrics/` (gather and/or push paths).
- `autoscaler/resources/PostgreSqlFlexibleServer/`, `MySqlFlexibleServer/`, `MsSqlDatabase/`, `MssqlElasticPool/` resource states.
- `docs/custom-metrics.md` and SQL resource docs under `docs/resources/`.

## Out of scope
- Log I/O mirroring on read-only replicas (ticket exclusion).
- Non-SQL resource types (AKS, VMSS, Azure DevOps, etc.).
- Replacing existing Azure Monitor scaling metrics where they already work; ticket targets synthetic/query-backed publication for portal parity.
- Remote issue tracking, PR, or CI wiring (`prHost: local`).

## Unknowns
- Exact YAML/config shape for QUERY (connection string source, timeout, result column, read-only enforcement).
- Whether QUERY metrics feed scaling (`Metrics`), push-only (`CustomMetrics`), or both.
- Authoritative portal metric definitions and DMVs/views per engine for DTU/CPU/memory/data I/O.
- Authentication model for SQL connections from the autoscaler identity (AAD vs SQL auth vs optional secret config).

## Tensions
- Ticket says “synthetic metrics” and “QUERY”; product code uses `CustomMetrics`/`DataExpression` and `custom_*` + `CustomMetric()` with no QUERY hook—terminology and extension point need explore/propose alignment.
