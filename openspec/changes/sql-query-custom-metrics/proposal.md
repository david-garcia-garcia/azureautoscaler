## Why

Operators need Azure Monitor series that match portal-visible SQL signals (DTU, CPU, memory, data I/O) when built-in metrics are missing, delayed, or differ on read replicas. The product already publishes operator-defined numbers through `CustomMetrics`; it has no QUERY value source, and honouring “synthetic metrics” as a second config tree would duplicate that job.

## What Changes

- Extend `CustomMetricConfig` with an optional `Query` string that is exclusive with `DataExpression` (XOR). Do not add a `SyntheticMetrics` type.
- `Name` stays required only for `DataExpression` rows. A `Query` row runs once per due cycle; the first result row’s numeric columns become Azure Monitor metrics named after those columns. Non-numeric columns are ignored.
- Publish QUERY results through `CustomMetricsPusher` only. Do not implement SQL `CustomMetric()` and do not replace working Azure Monitor scaling metrics.
- App builds the SQL connection (host from refreshed ARM FQDN + implied catalog + `ApplicationIntent=ReadOnly`) and authenticates with the process `TokenCredential` from `Program`. No YAML `Database`, connection-string, or SQL password field.
- Implied catalogs: Azure SQL Database = database name already in the ARM ResourceId; Elastic Pool = `master`; PostgreSQL Flexible Server = `postgres`; MySQL Flexible Server = `mysql`.
- QUERY is allowed only on those four SQL resource types. Query timeout defaults to ~30s (same order as the pusher HTTP timeout).
- Operators own QUERY text, including `replica_role` zeros and omitting log I/O. The app does not rewrite SQL.
- Docs: `CREATE USER FROM EXTERNAL PROVIDER` plus `GRANT VIEW DATABASE STATE`; Azure SQL examples may show the portal-mirror pattern (CPU / data I/O / memory / DTU as max(cpu, data_io), no log I/O).

## Capabilities

### New Capabilities

- `core_metrics_custom_query`: QUERY-backed custom metrics on the four SQL resource types — config XOR, implied catalog, first-row numeric columns pushed through `CustomMetricsPusher`, read-only TokenCredential sessions, operator-owned SQL.

### Modified Capabilities

_(none)_

## Impact

- `autoscaler/configuration/CustomMetricConfig.cs` and `Configuration.PrepareAndValidate`: XOR validation; `Name` required only for `DataExpression`.
- `autoscaler/metrics/CustomMetricsPusher.cs`: execute `Query` once when due; push one metric per numeric column; keep the existing per-metric catch (failed QUERY skips that push group).
- New SQL client packages and a connection builder used only by the QUERY path (no SQL stack exists in `autoscaler/` today).
- Four SQL resource states supply host/catalog facts after Refresh; they do not gain a `CustomMetric()` implementation.
- Docs: `docs/custom-metrics.md` and the four SQL files under `docs/resources/`.
- Tests: validation XOR, catalog implication, first-row/numeric mapping, connection `ApplicationIntent=ReadOnly`, and “no SQL `CustomMetric()`” coverage.
