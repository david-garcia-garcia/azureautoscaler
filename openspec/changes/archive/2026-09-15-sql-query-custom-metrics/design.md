## Context

See `proposal.md` for motivation. Today `CustomMetricConfig` requires `Name` + `DataExpression`; `Configuration.PrepareAndValidate` compiles the expression; `CustomMetricsPusher.PushIfDueAsync` evaluates `DataExpressionDelegate` and POSTs one series per config row. `HttpClient.Timeout` is already 30s. No SQL client package exists. The four SQL resource states refresh ARM objects and parse ResourceId parts (`serverName`, `databaseName`, …) but do not store a SQL FQDN or open a database session. `CustomMetric()` on those types still throws. Identity is the process `TokenCredential` constructed in `Program` and already passed into the pusher.

Research used: `knowledge/research/ext_azure-sql_dmvs_dm-db-resource-stats/`, `ext_azure-sql_connections_application-intent/`, `ext_azure-sql_auth_external-provider/`. Devdocs catalog is empty; no Language write (unattended, no unambiguous new term).

## Goals / Non-Goals

**Goals:**
- One value-source XOR on the existing CustomMetrics row.
- One SQL session factory owned by the push path, not by `CustomMetric()`.
- Connection facts (host, catalog, engine) derived from ResourceId + refreshed ARM, never from new YAML knobs.
- First-row numeric columns mapped to Monitor metric names at the pusher, reusing the existing POST body.

**Non-Goals:**
- Implementing SQL `CustomMetric()` or a second config tree.
- Baking default portal-mirror SQL.
- Rewriting operator SQL for replica role or log I/O.
- YAML `Database`, connection string, or SQL secret fields.
- Renaming `CustomMetric` / `CustomMetrics` or existing kebab live-spec folders.

## Decisions

### D1: Extend CustomMetricConfig; no SyntheticMetrics

**Decision:** Add `Query` (string) and optional `QueryTimeout` (duration, default `30s`) on `CustomMetricConfig`. Validate XOR with `DataExpression`. Keep `Namespace`, `ResourceId`, and `Frequency` on the same row (they already mean publish target / cadence). `Name` required only for `DataExpression`.

**Alternatives considered:**
- *New `SyntheticMetrics` list* — parallel publication unit next to CustomMetrics. Rejected (`skill:opd-commandments:Consume before produce`; deviation taken).
- *Reuse `Name` as the only published metric and require a single scalar column* — rejected; requester wants one Query to feed the portal-mirror set.

### D2: CustomMetricsPusher owns Query execution

**Decision:** When a due row has `Query`, the pusher runs the SQL once, then calls the existing `PushMetricAsync` once per numeric column (`metric.Name` = column name). Due-key for a Query row is `{resourceState.ResourceId}|query|{stable-row-id}` (row index in `CustomMetrics`, not `Name`). `DataExpression` rows keep `{resourceId}|{Name}`. Failed Query is caught by the existing per-row try/catch and skips that group. Empty / all-non-numeric result continues (no push).

Do not call or implement `ResourceState.CustomMetric()` on the SQL types.

**Alternatives considered:**
- *Implement Query inside `CustomMetric()`* — that owner is in-process gather for `custom_*` scaling names (Azure DevOps only). Wrong job.
- *New pusher type* — unnecessary; the REST POST and namespace fallback already live here.

### D3: One SQL query session factory; four engines

**Decision:** Add a single owner (new type in `autoscaler/metrics/`) that, given a refreshed `ResourceState` + process `TokenCredential` + Query text + timeout, opens a read session and returns the first row’s name/value pairs. Engine packages: `Microsoft.Data.SqlClient` (Azure SQL Database + Elastic Pool), `Npgsql` (PostgreSQL Flexible Server), `MySqlConnector` (MySQL Flexible Server). Token scopes: Azure SQL `https://database.windows.net/.default`; PostgreSQL/MySQL Flexible `https://ossrdbms-aad.database.windows.net/.default`. Username for Entra on PostgreSQL/MySQL is the token’s display/app name when the driver requires it; Azure SQL uses `AccessToken` / Entra auth on the connection (no SQL password).

Resource type comes from the existing `ResourceStateFactory` regexes (already the type owner). Query validation in `PrepareAndValidate` uses those same regexes on each instance ResourceId.

**Alternatives considered:**
- *Per-resource-state `RunQuery` overrides* — four copies of “open + first row”. Rejected (`One job, one owner`).
- *One generic ADO.NET path* — drivers and Entra token attachment differ enough that a thin engine switch is clearer than a forced abstraction.

### D4: Host and catalog are implied

**Decision:**

| Type | Host | Catalog |
|------|------|---------|
| Azure SQL Database | FullyQualifiedDomainName of the logical server (from refreshed ARM / parent server), not a YAML host | `ResourceParts["databaseName"]` |
| Azure SQL Elastic Pool | Same logical-server FQDN | `master` |
| PostgreSQL Flexible Server | Server ARM FQDN already loaded on refresh | `postgres` |
| MySQL Flexible Server | Server ARM FQDN already loaded on refresh | `mysql`

Azure SQL Database and Elastic Pool connection strings SHALL include `ApplicationIntent=ReadOnly` (Learn read scale-out). That keyword is a SqlClient routing hint; it is not applied to Npgsql/MySqlConnector (those engines have no equivalent connection-string token in this product). The app still does not rewrite SQL.

If FQDN or type cannot be resolved after Refresh, skip that Query group and log (same catch policy).

**Alternatives considered:**
- *YAML `Database`* — requester rejected.
- *Construct `{server}.database.windows.net` without ARM* — Explore said host from refreshed ARM FQDN; ARM is the owner of the name.

### D5: Numeric column detection

**Decision:** Treat a column as numeric when the provider type is a CLR numeric type (`byte`/`short`/`int`/`long`/`float`/`double`/`decimal`) or the first-row value is a boxed number / numeric `IConvertible`. Null numeric values skip that column (do not publish). Strings, dates, guids, and bools are ignored. `ConvertToDouble` already on the pusher stays the POST conversion.

**Alternatives considered:**
- *Publish everything `double.TryParse` accepts* — would turn timestamps and replica labels into metrics. Rejected.

### D6: Docs own grants and sample SQL

**Decision:** Update `docs/custom-metrics.md` plus the four SQL resource docs. Document XOR, implied catalogs, 30s timeout, ReadOnly + TokenCredential, `CREATE USER FROM EXTERNAL PROVIDER` + `GRANT VIEW DATABASE STATE`, and that Elastic Pool `master` is the wrong catalog for `sys.dm_db_resource_stats` (use `sys.elastic_pool_resource_stats` / operator SQL). Sample Azure SQL may zero when `replica_role <> 1` and compute DTU as `max(cpu, data_io)` without log I/O. Product does not ship those strings as defaults.

## Risks / Trade-offs

- **ApplicationIntent does not guarantee a replica** → sessions may land on primary (Basic/Standard/GP, Hyperscale with zero secondaries). Mitigation: docs + operator `replica_role` zeros. App does not filter.
- **Elastic Pool + `sys.dm_db_resource_stats`** → measures `master`. Mitigation: docs; operators must query pool views in `master`.
- **New SQL drivers enlarge the binary** → accepted; no existing client to reuse.
- **Entra username for PostgreSQL/MySQL** → some drivers need a user name besides the token. Mitigation: use the token identity display/app name; if missing, skip and log.
- **Query timeout vs HTTP timeout** → both ~30s. A slow Query can occupy the push loop until timeout; same as today’s per-row catch.

## Migration Plan

- Existing CustomMetrics with only `DataExpression` keep working (XOR allows that shape).
- Operators add `Query` rows on the four SQL types; no config rename.
- Rollback: remove `Query` rows and the new packages; DataExpression path unchanged.

## Open Questions

None that change the spec or task breakdown. Remaining explore row “push-only vs also `CustomMetric()`” is taken as push-only (see `devstate/explore.md`).
