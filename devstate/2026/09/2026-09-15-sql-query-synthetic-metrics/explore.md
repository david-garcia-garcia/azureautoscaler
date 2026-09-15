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
- **Azure SQL Elastic Pool.** `MssqlElasticPoolResourceState` (`autoscaler/resources/MssqlElasticPool/`). Refresh reads Monitor `storage_used`. Docs (`docs/resources/sql-elastic-pool.md`) use native `dtu_consumption_percent`, `eDTU_used`, `eDTU_limit`, `storage_used`. A pool ARM id is not a SQL database endpoint. Catalog for QUERY defaults to `master` on the logical server (no extra YAML `Database` field). Operator SQL that uses `sys.dm_db_resource_stats` is current-database scoped (Learn: `sys.dm_db_resource_stats`); on `master` that measures master, not the pool.
- **No SQL wire stack.** `autoscaler/poolautoscaler.csproj` has ARM + `Azure.Monitor.Query` only. Adding engine drivers is a new unit, not a wrap of an existing one.
- **Replica / log I/O.** `not found` under `autoscaler/resources/` for replica, read-only role, or log I/O. Wildcard SQL Database expansion can include any database resource (`MsSqlDatabaseResourceStateHelper.ExpandSqlDatabaseWildcard`); there is no replica filter. Human: app always builds QUERY connections with `ApplicationIntent=ReadOnly`; that does not guarantee a replica (Basic/Standard/General Purpose have none — Learn read scale-out). Operator SQL uses `replica_role` and must emit zeros when the session is not an HA secondary (`replica_role = 1`).
- **`sys.dm_db_resource_stats`.** Official Learn: returns CPU, I/O, and memory for **a database**; example is “currently connected database”; needs `VIEW DATABASE STATE`. Not a `master`/server catalog. `sys.resource_stats` (different view, 5-minute / 14-day) lives in `master`. `replica_role`: 0 primary, 1 HA secondary, 2 geo forwarder, 3 named replica; reports 1 when connected with ReadOnly intent to a readable secondary. Official sample DTU percent is `max(cpu, data_io, log_write)`; this ticket excludes log I/O.
- **Catalogs.** Research write delegated this session (`ext_azure-sql_dmvs_dm-db-resource-stats`, `ext_azure-sql_connections_application-intent`). Devdocs index still absent. No Language writes.

## Decisions

- Treat ticket “synthetic metrics” as the existing `CustomMetrics` surface plus a `Query` value source. Do not add `SyntheticMetrics`. Proposed deviation recorded (requester still not asked).
- QUERY is a value source on `CustomMetricConfig`, exclusive with `DataExpression` (XOR). One Query runs **once** per due cycle and may return many named columns; each **numeric** column is published as its own Azure Monitor metric using the column name. `Name` stays required only for `DataExpression` rows. Existing runtime sites enumerated: `Configuration.PrepareAndValidate` (`autoscaler/configuration/Configuration.cs`) and `CustomMetricsPusher.EvaluateExpression` (`autoscaler/metrics/CustomMetricsPusher.cs`). Rank: `bounded asked` — human asked for multi-dimension QUERY; both sites can be migrated here.
- This change publishes QUERY results through `CustomMetricsPusher`. It does not implement SQL `CustomMetric()` names and does not replace working Azure Monitor scaling metrics (Out of scope).
- Operators own QUERY text (including replica_role zeroing and excluding log I/O). Product does not bake unsigned default SQL. Docs may show the Azure SQL examples the requester pasted. Docs must also say the identity needs a SQL login (AAD/external provider) plus `VIEW DATABASE STATE` (Learn on `sys.dm_db_resource_stats`).
- SQL session identity reuses the process `TokenCredential` from `Program`. The app builds the connection string (host + implied catalog + `ApplicationIntent=ReadOnly`). No connection-string or SQL password field.
- No YAML `Database` field. Catalog is implied: Azure SQL Database → database name already in the ARM `ResourceId`; Elastic Pool → `master` on the logical server; PostgreSQL Flexible Server → `postgres`; MySQL Flexible Server → `mysql`.
- Do not silently rename `CustomMetric` / `CustomMetrics` (Issues).

## Open questions

- Q: What YAML fields does QUERY take (text, database name, timeout, result column)?
  Rank: additive asked — new fields on `CustomMetricConfig` this change would create; requirement Unknowns and Desired name QUERY config
  Decision: resolved — optional `Query` string XOR `DataExpression`; no `Database` field and no connection-string field; optional timeout defaulting to ~30s (`CustomMetricsPusher` HTTP timeout). Catalog is implied by the resource (see host/database row). Human rejected an extra Database knob.
  By: explore

- Q: Do QUERY metrics feed scaling (`Metrics` / `custom_*`), push-only (`CustomMetrics`), or both?
  Rank: additive asked — Desired says published / portal-mirror; Out of scope says do not replace working Monitor scaling metrics
  Decision: assumed — push-only via `CustomMetrics`. Operators who want to scale on the published series can already set `Metric.Namespace` to the custom namespace (`docs/configuration.md`). Do not wire QUERY into SQL `CustomMetric()`.
  By: explore

- Q: What are the authoritative portal formulas / DMVs per engine for DTU, CPU, memory, and data I/O?
  Rank: additive incidental — Desired asks for the ability to define QUERY, not for shipped default SQL; no criterion names baked-in DMVs
  Decision: resolved — operators supply the SQL. Docs may include the Azure SQL `sys.dm_db_resource_stats` examples the requester pasted (CPU / data I/O / memory / DTU as max(cpu, data_io), no log I/O). Product does not execute a hardcoded default. Official Learn DTU sample uses max(cpu, data_io, log_write); this change follows the requester and omits log I/O.
  By: explore

- Q: Who already owns the identity used to open a SQL session?
  Rank: additive asked — Unknowns name AAD vs SQL auth; QUERY needs a connection owner
  Decision: resolved — reuse the process `TokenCredential` from `Program` (`autoscaler/Program.cs`). No SQL password/secret config. Docs must state the extra SQL-side grant: create the same identity as a user inside the database (AAD / `CREATE USER FROM EXTERNAL PROVIDER` or current official equivalent) and grant `VIEW DATABASE STATE` (Learn on `sys.dm_db_resource_stats`). ARM `Monitoring Metrics Publisher` is not enough for the QUERY login.
  By: explore

- Q: What host and database does Elastic Pool (and Flexible Server) QUERY connect to?
  Rank: additive asked — In-scope includes Elastic Pool; a pool ResourceId is not a SQL database
  Decision: assumed — no YAML `Database`. Host from refreshed ARM FQDN. Catalog: Azure SQL Database → database name already in the ARM id (not `master`); Elastic Pool → `master`; PostgreSQL → `postgres`; MySQL → `mysql`. Requester asked why Database if metrics live on master. Learn: `sys.dm_db_resource_stats` is current-database; `sys.resource_stats` is the master view. Connecting a SQL Database resource to `master` would measure master, not the portal series for that database. Needs requester confirm.
  By: explore

- Q: How is “not log I/O on read-only replicas” enforced?
  Rank: additive asked — Desired and Out of scope name that exclusion
  Decision: resolved — product does not filter replicas and does not ship log I/O columns. Operator SQL omits log I/O. For replica-targeted QUERY, the operator SQL uses `replica_role` and returns zero for every published numeric column when the session is not an HA secondary (`replica_role = 1`). App does not rewrite SQL.
  By: explore

- Q: How is the QUERY session kept read-only?
  Rank: additive asked — requirement Unknowns name read-only enforcement
  Decision: resolved — the app always builds the SQL connection string with `ApplicationIntent=ReadOnly` (Learn read scale-out). That routes to a readable secondary when the tier has one; it does not guarantee a replica (Basic/Standard/General Purpose have none). Do not parse or rewrite operator SQL. Failed QUERY skips that push group (same per-metric catch as `CustomMetricsPusher`).
  By: explore

- Q: Must QUERY return a single scalar, or may it return a named column / many rows?
  Rank: additive asked — Unknowns name result column; publication needs one number per `CustomMetricConfig.Name`
  Decision: resolved — first row only. Each numeric column is one metric; metric name = column name. Non-numeric columns (`from_time`, `to_time`, strings) are ignored (not used as Azure Monitor timestamps in this change). Empty or all-non-numeric result skips the push. One Query entry replaces many one-name rows for the portal-mirror set.
  By: explore

## Catalogs

- `knowledge/devdocs/index.md`: **not found** (empty catalog).
- `knowledge/research/index.md`: write in progress this session (`ext_azure-sql_dmvs_dm-db-resource-stats`, `ext_azure-sql_connections_application-intent`).
- No `priority: always` packets. No Language/usage writes.
- Official Learn consumed on this thread for `sys.dm_db_resource_stats` and ApplicationIntent (research folder delegated).

## Verdict

`in progress` — gap reproduced; human refined QUERY shape (multi-column, no Database field, ApplicationIntent + replica_role zeros). No `blocked` row; no `structural incidental` rank (no `explore_decide/` pass). Waiting on confirmation of the master-vs-current-database catalog for Azure SQL Database resources.
