## 1. Config surface

- [ ] 1.1 Add `Query` and optional `QueryTimeout` (default 30s) on `CustomMetricConfig`; keep `DataExpression` / `Name` / `Namespace` / `ResourceId` / `Frequency`
- [ ] 1.2 Change `Configuration.PrepareAndValidate` so each CustomMetrics row is `Query` XOR `DataExpression`; require `Name` only for `DataExpression`; reject both, neither, or blank sources
- [ ] 1.3 Reject `Query` unless every instance ResourceId on that resource matches one of the four SQL `ResourceStateFactory` regexes (Azure SQL Database, Elastic Pool, PostgreSQL Flexible Server, MySQL Flexible Server)
- [ ] 1.4 Do not add `Database`, connection-string, or SQL password fields

## 2. SQL session factory

- [ ] 2.1 Add `Microsoft.Data.SqlClient`, `Npgsql`, and `MySqlConnector` to `autoscaler/poolautoscaler.csproj`
- [ ] 2.2 Add one owner in `autoscaler/metrics/` that, given refreshed `ResourceState` + process `TokenCredential` + Query + timeout, opens the session and returns the first row’s name/value pairs
- [ ] 2.3 Resolve host from refreshed ARM FQDN (logical server for Azure SQL Database / Elastic Pool; flexible-server FQDN for PostgreSQL / MySQL); skip and log if FQDN is missing
- [ ] 2.4 Apply implied catalogs: SQL Database = `ResourceParts["databaseName"]`; Elastic Pool = `master`; PostgreSQL = `postgres`; MySQL = `mysql`
- [ ] 2.5 Azure SQL connections include `ApplicationIntent=ReadOnly` and authenticate with TokenCredential scope `https://database.windows.net/.default`; PostgreSQL/MySQL use TokenCredential scope `https://ossrdbms-aad.database.windows.net/.default` (no ApplicationIntent keyword)
- [ ] 2.6 Command timeout defaults to 30s (`QueryTimeout` when set); do not parse or rewrite operator SQL

## 3. Push path

- [ ] 3.1 In `CustomMetricsPusher.PushIfDueAsync`, when a due row has `Query`, run the factory once; do not require `DataExpressionDelegate`
- [ ] 3.2 Publish each numeric first-row column via existing `PushMetricAsync` using the column name as the metric name; ignore non-numeric columns; skip the row when the result is empty or all non-numeric
- [ ] 3.3 Due-key for Query rows is `{resourceId}|query|{row-index}`; keep `{resourceId}|{Name}` for `DataExpression`
- [ ] 3.4 Keep the existing per-row try/catch so a failed Query skips that group only
- [ ] 3.5 Do not implement SQL `CustomMetric()` on the four SQL resource states

## 4. Tests

- [ ] 4.1 Validation: XOR, `Name` only for `DataExpression`, Query allowed on the four SQL types, Query rejected on a non-SQL ResourceId
- [ ] 4.2 Session factory: implied catalogs, Azure SQL connection string contains `ApplicationIntent=ReadOnly`, timeout default 30s, first-row-only, numeric vs ignored columns (fake/in-memory reader — no live SQL)
- [ ] 4.3 Pusher: one Query run yields N POSTs named after numeric columns; empty/non-numeric skips; factory throw is caught and other CustomMetrics continue
- [ ] 4.4 Assert SQL `CustomMetric()` still throws `NotImplementedException` on the four SQL types

## 5. Docs

- [ ] 5.1 Update `docs/custom-metrics.md`: Query XOR DataExpression, no `Name` on Query rows, first-row numeric columns, 30s timeout, implied catalogs, TokenCredential + ReadOnly, `CREATE USER FROM EXTERNAL PROVIDER` + `GRANT VIEW DATABASE STATE`
- [ ] 5.2 Update `docs/resources/sql-database.md`, `sql-elastic-pool.md`, `postgresql-flexible-server.md`, `mysql-flexible-server.md` with a Query example; Elastic Pool docs MUST warn that `sys.dm_db_resource_stats` on `master` measures `master`, not the pool
- [ ] 5.3 Azure SQL examples MAY zero numeric columns when `replica_role <> 1` and compute DTU as max(cpu, data_io) without log I/O; do not ship those strings as product defaults
