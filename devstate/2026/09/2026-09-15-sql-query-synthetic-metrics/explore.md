# Explore

## Reproduce

Status: **reproduced** on this workspace’s product tree (merge-base with `1.x` is `a3f37c5`; `1.x...HEAD` product diff is empty aside from `devstate/`).

Throwaway console under `%TEMP%\explore-sql-query-synthetic-repro` referenced `autoscaler/poolautoscaler.csproj` and ran the live types:

| Path | Result |
|------|--------|
| `PostgreSqlFlexibleServerResourceState.CustomMetric` | `NotImplementedException`: `Custom metric not implemented: custom_dtu` |
| `MySqlFlexibleServerResourceState.CustomMetric` | same message |
| `MsSqlDatabaseResourceState.CustomMetric` | `NotImplementedException`: `CustomMetric` (base) |
| `MssqlElasticPoolResourceState.CustomMetric` | same base message |
| `CustomMetricConfig` public properties | `DataExpression`, `DataExpressionDelegate`, `Frequency`, `FrequencyParsed`, `Name`, `Namespace`, `ResourceId` — **no `Query`** |
| `Configuration.PrepareAndValidate` with `Name` only | `Custom metric 'portal_dtu' must have a DataExpression.` |

Repo grep: `synthetic` appears only under this run’s `devstate/` (not product). `autoscaler/configuration/` has no QUERY/Query field — only `using Azure.Monitor.Query.Models`. No `Npgsql` / `MySqlConnector` / `Microsoft.Data.SqlClient` package or type in `autoscaler/`.

Existing tests never call SQL `CustomMetric()` (`autoscalertests/` only implements it on `TestResourceState`). No product test was added.

## Product job

Operators need **QUERY-backed custom metrics** on the four SQL resource types so they can **publish** portal-mirror signals (DTU, CPU, memory, data I/O) into Azure Monitor. Log I/O on read-only replicas is out of scope.

The ticket word “synthetic” is not a product type. The unit that already publishes operator-defined numbers is `CustomMetrics` + `CustomMetricsPusher`. Honouring “synthetic” as a second config tree would add a parallel abstraction next to that unit.

```
  config.yml
       │
       ├─ CustomMetrics[] ── DataExpression ──► CustomMetricsPusher ──► Azure Monitor REST
       │                         ▲
       │                    (QUERY missing)
       │
       └─ ScalingConfigurations.Metrics
                │
                ├─ name custom_* ──► ResourceState.CustomMetric() ── SQL: stub / base throw
                └─ else ──────────► MetricsQueryClient (Azure Monitor)
```

Publication is the commissioned job. In-process `custom_*` gather is a different owner (`CustomMetric()`), implemented only for Azure DevOps.

## Concepts

- **CustomMetrics (push).** `Resource.CustomMetrics` (`autoscaler/configuration/Resource.cs`) is a list of `CustomMetricConfig` (`autoscaler/configuration/CustomMetricConfig.cs`). `Configuration.PrepareAndValidate` (`autoscaler/configuration/Configuration.cs`) requires `Name` and `DataExpression`, parses `Frequency` (default `5m`), compiles `DataExpression` to `DataExpressionDelegate`.
- **CustomMetricsPusher.** `autoscaler/metrics/CustomMetricsPusher.cs`. After `Refresh`, `ResourceProcessor.RunLoop` (`autoscaler/resourcemanagement/ResourceProcessor.cs`) calls `PushIfDueAsync`. Builds one `CustomMetricDataContext` via `ICustomMetricDataProvider.BuildCustomMetricDataContextAsync`, evaluates `DataExpressionDelegate` only (`EvaluateExpression` returns null if the delegate is missing), POSTs to `https://{region}.monitoring.azure.com/{resourceId}/metrics` with `TokenCredential` scope `https://monitoring.azure.com/.default`. Namespace: per-metric, else `Configuration.CustomMetricsNamespace`, else `"Custom Autoscaler"`.
- **CustomMetricDataContext.** `autoscaler/metrics/Dto/CustomMetricDataContext.cs`: `Resource`, `ExistingState`, `ResourceParts`, `Helpers`, `Extra`. Base `ResourceState.BuildCustomMetricDataContextAsync` (`autoscaler/resourcemanagement/ResourceState.cs`) aborts if `GetCustomMetricExtraAsync` returns null; throws if `SubscriptionId` or `Location` is missing (VM-size helpers). Base Extra is empty `{}`. Only AKS overrides Extra (`autoscaler/resources/AksNodePool/AksNodePoolResourceState.cs`). SQL types use the base Extra.
- **Azure Monitor gather.** `AzureMonitorMetricsGatherer` (`autoscaler/metrics/AzureMonitorMetricsGatherer.cs`): if `Metric.Name` starts with `custom_`, call `state.CustomMetric(...)`; else `MetricEvaluation.RetrieveHistory` via `MetricsQueryClient`. Optional `Metric.Namespace` reads back a previously pushed custom namespace (`docs/configuration.md`, `autoscaler/configuration/Metric.cs`).
- **CustomMetric() (in-process gather).** Base throws `NotImplementedException("CustomMetric")`. PostgreSQL/MySQL override and throw for every name. Azure SQL Database and Elastic Pool inherit the base. Azure DevOps is the only real implementation (`autoscaler/resources/AzureDevops/AzureDevOpsParallelJobsResourceState.cs`). Out of scope for non-SQL types.
- **Identity owner.** `Program` (`autoscaler/Program.cs`) constructs the process `TokenCredential` (`DefaultAzureCredential`, `DeviceCodeCredential`, or debugger `InteractiveBrowserCredential`). `ResourceProcessor` passes that same credential into gather, push, and refresh. No SQL username/password or connection-string config exists.
- **PostgreSQL Flexible Server.** Factory regex + `PostgreSqlFlexibleServerResourceState` (`autoscaler/resourcemanagement/ResourceStateFactory.cs`, `autoscaler/resources/PostgreSqlFlexibleServer/`). Refresh loads ARM server, sets `Location` from `Data.Location`, SKU/IOPS/cores. Docs already show `CustomMetrics` via `DataExpression` on SKU (`docs/resources/postgresql-flexible-server.md`). Scaling examples use native Monitor names (`cpu_percent`). No replica field, no SQL client.
- **MySQL Flexible Server.** Same shape (`autoscaler/resources/MySqlFlexibleServer/`, `docs/resources/mysql-flexible-server.md`). Native scaling metrics include `cpu_percent`, `storage_io_count`, `io_consumption_percent`.
- **Azure SQL Database.** `MsSqlDatabaseResourceState` (`autoscaler/resources/MsSqlDatabase/`). Refresh loads ARM database and reads Monitor metric `storage` for `CurrentUsedStorage`. Does not set `Location` itself; `ResourceState.Refresh` may copy `armResource.Id.Location`. Docs (`docs/resources/sql-database.md`) show native `dtu_consumption_percent` / `allocated_data_storage` — no `CustomMetrics` examples. `Metric.AllowFail` comment already notes `dtu_consumption_percent` is DTU-model-only.
- **Azure SQL Elastic Pool.** `MssqlElasticPoolResourceState` (`autoscaler/resources/MssqlElasticPool/`). Refresh reads Monitor `storage_used`. Docs (`docs/resources/sql-elastic-pool.md`) use native `dtu_consumption_percent`, `eDTU_used`, `eDTU_limit`, `storage_used`. A pool ARM id is not a SQL database endpoint; QUERY needs a member database name (open question).
- **No SQL wire stack.** `autoscaler/poolautoscaler.csproj` has ARM + `Azure.Monitor.Query` only. Adding engine drivers is a new unit, not a wrap of an existing one.
- **Replica / log I/O.** `not found` under `autoscaler/resources/` for replica, read-only role, or log I/O. Wildcard SQL Database expansion can include any database resource (`MsSqlDatabaseResourceStateHelper.ExpandSqlDatabaseWildcard`); there is no replica filter.
- **Catalogs.** `knowledge/devdocs/index.md` and `knowledge/research/index.md` are absent. Store roots are empty (only untracked `knowledge/opendev.json`, not a catalog). No Language writes. Vendor DMV/portal formulas were not sourced.

## Decisions

- Treat ticket “synthetic metrics” as the existing `CustomMetrics` surface plus a `Query` value source. Do not add `SyntheticMetrics`. Proposed deviation recorded.
- QUERY is a value source on `CustomMetricConfig`, exclusive with `DataExpression` (XOR). Making `DataExpression` optional changes an existing required contract. Existing runtime sites enumerated: `Configuration.PrepareAndValidate` (`autoscaler/configuration/Configuration.cs`) and `CustomMetricsPusher.EvaluateExpression` (`autoscaler/metrics/CustomMetricsPusher.cs`). Docs that name required `DataExpression`: `docs/custom-metrics.md`, `docs/resources/postgresql-flexible-server.md`, `docs/resources/mysql-flexible-server.md`, plus AKS examples. Rank of that reshape: `bounded asked` — Desired says values come from QUERY, not only `DataExpression`; both sites can be migrated here.
- This change publishes QUERY results through `CustomMetricsPusher`. It does not implement SQL `CustomMetric()` names and does not replace working Azure Monitor scaling metrics (Out of scope).
- Operators own QUERY text. Product does not ship unsigned default DTU/CPU/memory/data-I/O SQL. Catalogs empty; vendor DMVs not invented.
- SQL session identity reuses the process `TokenCredential` already owned by `Program`. Do not add a second identity or connection-string secret unless a later row changes.
- Do not silently rename `CustomMetric` / `CustomMetrics` (Issues).

## Open questions

- Q: What YAML fields does QUERY take (text, database name, timeout, result column)?
  Rank: additive asked — new fields on `CustomMetricConfig` this change would create; requirement Unknowns and Desired name QUERY config
  Decision: assumed — optional `Query` string XOR `DataExpression`; optional `Database` (required at runtime for Elastic Pool and when the server has more than one usable DB); optional timeout defaulting to the pusher HTTP timeout order of magnitude (30s in `CustomMetricsPusher`); value = first row, first numeric column. No connection-string field.
  By: explore

- Q: Do QUERY metrics feed scaling (`Metrics` / `custom_*`), push-only (`CustomMetrics`), or both?
  Rank: additive asked — Desired says published / portal-mirror; Out of scope says do not replace working Monitor scaling metrics
  Decision: assumed — push-only via `CustomMetrics`. Operators who want to scale on the published series can already set `Metric.Namespace` to the custom namespace (`docs/configuration.md`). Do not wire QUERY into SQL `CustomMetric()`.
  By: explore

- Q: What are the authoritative portal formulas / DMVs per engine for DTU, CPU, memory, and data I/O?
  Rank: additive incidental — Desired asks for the ability to define QUERY, not for shipped default SQL; no criterion names baked-in DMVs
  Decision: assumed — operators supply the SQL; docs may show placeholders only. Not sourced (research catalog empty). Do not invent vendor facts.
  By: explore

- Q: Who already owns the identity used to open a SQL session?
  Rank: additive asked — Unknowns name AAD vs SQL auth; QUERY needs a connection owner
  Decision: assumed — reuse the process `TokenCredential` from `Program` (`autoscaler/Program.cs`). That owner already supplies ARM, Monitor gather, and custom-metric push. Engine token audiences are implement-time; no SQL password/secret config.
  By: explore

- Q: What host and database does Elastic Pool (and Flexible Server) QUERY connect to?
  Rank: additive asked — In-scope includes Elastic Pool; a pool ResourceId is not a SQL database
  Decision: assumed — host comes from the refreshed ARM resource for the configured `ResourceId` (FQDN is not read in product code today — `not found` `FullyQualifiedDomainName` under `autoscaler/resources/`); `Database` on the metric selects the catalog. Do not invent a second resource type for “member database”.
  By: explore

- Q: How is “not log I/O on read-only replicas” enforced?
  Rank: additive asked — Desired and Out of scope name that exclusion
  Decision: assumed — no replica-role detector exists (`not found` in `autoscaler/resources/`). Operators target the ResourceId they want and omit log I/O QUERY. Product does not add log I/O defaults or a replica filter.
  By: explore

- Q: How is the QUERY session kept read-only?
  Rank: additive asked — requirement Unknowns name read-only enforcement
  Decision: assumed — open the driver session read-only when the client API supports it; do not parse or rewrite operator SQL. A failed QUERY skips that metric push (same per-metric catch as `CustomMetricsPusher`).
  By: explore

- Q: Must QUERY return a single scalar, or may it return a named column / many rows?
  Rank: additive asked — Unknowns name result column; publication needs one number per `CustomMetricConfig.Name`
  Decision: assumed — first row, first numeric column. Extra rows/columns ignored. Non-numeric or empty result skips the push.
  By: explore

## Catalogs

- `knowledge/devdocs/index.md`: **not found** (empty catalog).
- `knowledge/research/index.md`: **not found** (empty catalog).
- No `priority: always` packets. No Language/usage writes (attended; “synthetic” parked above).
- No research folder written (operator-owned QUERY; vendor DMVs marked assumed).

## Verdict

`in progress` — gap reproduced; no `blocked` row; no `structural incidental` rank (no `explore_decide/` pass).
