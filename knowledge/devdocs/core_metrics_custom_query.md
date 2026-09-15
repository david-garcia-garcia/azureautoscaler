# Query CustomMetrics

## Language

**Query CustomMetrics row**:
A `CustomMetricConfig` whose value source is `Query` (exclusive with `DataExpression`).
_Avoid_: synthetic metric, SyntheticMetrics

**Implied catalog**:
The SQL database name the session opens, derived from resource type (ResourceId database, `master`, `postgres`, or `mysql`).
_Avoid_: Database YAML field, connection-string catalog

## Overview

Operators publish first-row numeric SQL columns through the existing CustomMetrics pusher. Implementers extend `CustomMetricConfig` and `SqlQuerySessionFactory`; they do not add a second config tree or implement SQL `CustomMetric()`.

## How to use

- Validate XOR in `Configuration.PrepareAndValidate`: exactly one of `Query` / `DataExpression`; require `Name` only for `DataExpression`; reject Query unless every instance ResourceId matches the four SQL factory regexes.
- Resolve host from refreshed ARM FQDN. Skip (throw for the pusher catch) when FQDN or SQL Database `databaseName` is missing.
- Open Azure SQL with `TrustServerCertificate=False`. Require `QueryConnection.ApplicationIntent` (`ReadOnly` or `ReadWrite`); do not inject it. Open PostgreSQL/MySQL with `SSL Mode=VerifyFull` / `SslMode=VerifyFull` and an Entra user name from the access token. Merge `QueryConnection` extras; reject reserved host/catalog/credential keys.
- Execute operator SQL once. Map the first row with `SqlQueryMetricSeriesMapper`. Bare numeric columns POST as one sample (`min` = `max` = `sum` = value, `count` = 1). Matching `_min`/`_max`/`_sum`/`_count` suffixes POST one stem with those series fields.
- Do not rewrite SQL for `replica_role` or log I/O.
- Do not implement `CustomMetric()` on the SQL resource states.

## Pattern snippet

Full replica example (standalone filter, `replica_*` names, `dtu_used`, QueryConnection): `docs/resources/sql-database.md`.

```yaml
    CustomMetrics:
      - Query: |
          SELECT
            MIN(avg_cpu_percent) AS replica_cpu_percent_min,
            MAX(avg_cpu_percent) AS replica_cpu_percent_max,
            SUM(avg_cpu_percent) AS replica_cpu_percent_sum,
            COUNT_BIG(*) AS replica_cpu_percent_count
          FROM sys.dm_db_resource_stats
          WHERE end_time >= DATEADD(minute, -1, SYSUTCDATETIME())
        Frequency: 1m
        QueryConnection:
          ApplicationIntent: ReadOnly
```

## Key files

- `autoscaler/configuration/CustomMetricConfig.cs`
- `autoscaler/metrics/SqlQuerySessionFactory.cs`
- `autoscaler/metrics/SqlQueryConnectionAttributes.cs`
- `autoscaler/metrics/CustomMetricsPusher.cs`
- `autoscaler/metrics/SqlQueryMetricSeriesMapper.cs`
- `autoscaler/metrics/CustomMetricSeries.cs`
- `docs/custom-metrics.md`
- `docs/resources/sql-database.md`

## Gotchas

- Azure SQL Query fails startup unless `QueryConnection.ApplicationIntent` is `ReadOnly` or `ReadWrite`. The factory does not inject it.
- Elastic Pool sessions use `master`. `sys.dm_db_resource_stats` there measures `master`, not the pool.
- `Monitoring Metrics Publisher` is not enough to run Query; the identity needs a SQL user and `VIEW DATABASE STATE`.
- PostgreSQL/MySQL fail closed when the token has no Entra user-name claim.
- Incomplete `_min`/`_max`/`_sum`/`_count` groups are skipped. `count` must be ≥ 1. A bare column with the same name as a suffix stem is not published as a one-sample metric.
