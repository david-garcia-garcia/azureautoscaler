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
- Open Azure SQL with `ApplicationIntent=ReadOnly` and `TrustServerCertificate=False`. Open PostgreSQL/MySQL with `SSL Mode=VerifyFull` / `SslMode=VerifyFull` and an Entra user name from the access token.
- Execute operator SQL once. Map the first row. Publish each numeric column via `CustomMetricsPusher.PushNamedMetricAsync` using the column name.
- Do not rewrite SQL for `replica_role` or log I/O.
- Do not implement `CustomMetric()` on the SQL resource states.

## Pattern snippet

```yaml
    CustomMetrics:
      - Query: |
          SELECT avg_cpu_percent AS cpu_percent
          FROM sys.dm_db_resource_stats
        Frequency: 5m
```

## Key files

- `autoscaler/configuration/CustomMetricConfig.cs`
- `autoscaler/metrics/SqlQuerySessionFactory.cs`
- `autoscaler/metrics/CustomMetricsPusher.cs`
- `docs/custom-metrics.md`

## Gotchas

- Elastic Pool sessions use `master`. `sys.dm_db_resource_stats` there measures `master`, not the pool.
- `Monitoring Metrics Publisher` is not enough to run Query; the identity needs a SQL user and `VIEW DATABASE STATE`.
- PostgreSQL/MySQL fail closed when the token has no Entra user-name claim.
