## Purpose

Lets operators publish QUERY-backed custom metrics on the four SQL resource types into Azure Monitor, using the existing CustomMetrics surface and column names as metric names.

## ADDED Requirements

### Requirement: Query XOR DataExpression on CustomMetrics
A CustomMetrics entry SHALL supply exactly one value source: `Query` or `DataExpression`. The product SHALL NOT add a `SyntheticMetrics` configuration type. `Name` SHALL be required only when the value source is `DataExpression`. A `Query` entry SHALL NOT require `Name`. Startup validation SHALL reject a CustomMetrics entry that has both sources, neither source, or a blank `Query`/`DataExpression`. Startup validation SHALL reject `Query` on any resource that is not Azure SQL Database, Azure SQL Elastic Pool, PostgreSQL Flexible Server, or MySQL Flexible Server.

#### Scenario: DataExpression row still requires Name
- **WHEN** a CustomMetrics entry has `DataExpression` and no `Query`
- **THEN** startup SHALL require `Name` and SHALL compile `DataExpression` as today

#### Scenario: Query row does not require Name
- **WHEN** a CustomMetrics entry has `Query` and no `DataExpression`
- **THEN** startup SHALL accept the entry without `Name`

#### Scenario: Both sources are rejected
- **WHEN** a CustomMetrics entry has both `Query` and `DataExpression`
- **THEN** startup SHALL fail validation

#### Scenario: Neither source is rejected
- **WHEN** a CustomMetrics entry has neither `Query` nor `DataExpression`
- **THEN** startup SHALL fail validation

#### Scenario: Query on a non-SQL resource is rejected
- **WHEN** a CustomMetrics entry has `Query` on a resource that is not one of the four SQL types
- **THEN** startup SHALL fail validation

### Requirement: No Database or connection-string YAML
CustomMetrics SHALL NOT accept a YAML `Database` field, a connection-string field, or a SQL username/password field. The app SHALL build the session from the refreshed ARM host, the implied catalog, and the process TokenCredential.

#### Scenario: Extra Database knob is not part of the contract
- **WHEN** an operator configures a Query CustomMetrics entry
- **THEN** the documented config surface SHALL omit `Database` and connection-string fields

### Requirement: Implied catalog by SQL resource type
The QUERY session catalog SHALL be implied by resource type and SHALL NOT be operator-configurable:

- Azure SQL Database: the database name already present in the ARM ResourceId
- Azure SQL Elastic Pool: `master` on the logical server
- PostgreSQL Flexible Server: `postgres`
- MySQL Flexible Server: `mysql`

#### Scenario: SQL Database uses the ResourceId database
- **WHEN** QUERY runs for an Azure SQL Database resource
- **THEN** the session catalog SHALL be the database name from that resource’s ARM ResourceId, not `master`

#### Scenario: Elastic Pool uses master
- **WHEN** QUERY runs for an Azure SQL Elastic Pool resource
- **THEN** the session catalog SHALL be `master` on the logical server

#### Scenario: Flexible servers use the engine default catalog
- **WHEN** QUERY runs for PostgreSQL Flexible Server
- **THEN** the session catalog SHALL be `postgres`
- **WHEN** QUERY runs for MySQL Flexible Server
- **THEN** the session catalog SHALL be `mysql`

### Requirement: One Query run publishes numeric columns from the first row
When a Query entry is due, the app SHALL execute that Query text once. It SHALL read only the first result row. Each numeric column SHALL become part of an Azure Monitor custom metric as specified here and in **Query series suffixes fill Azure Monitor min max sum count**. A numeric column that does not use a reserved series suffix SHALL be published as one metric whose name is that column name, with `min` = `max` = `sum` = the value and `count` = 1. Non-numeric columns SHALL be ignored and SHALL NOT be used as Azure Monitor timestamps. An empty result, or a first row with no numeric columns, SHALL skip publication for that entry without treating it as a process failure.

#### Scenario: Several numeric columns become several metrics
- **WHEN** a due Query returns a first row with numeric columns `cpu_percent` and `data_io_percent` and a non-numeric column `from_time`
- **THEN** the app SHALL publish `cpu_percent` and `data_io_percent` and SHALL ignore `from_time`

### Requirement: Query series suffixes fill Azure Monitor min max sum count
When first-row numeric columns use the reserved suffixes `_min`, `_max`, `_sum`, and `_count` on the same stem, the app SHALL publish one Azure Monitor custom metric named after that stem and SHALL set the POST series `min`, `max`, `sum`, and `count` from those columns. All four suffixes SHALL be present for that stem. An incomplete group, a duplicate field, or a `count` below 1 SHALL skip that stem. A bare column whose name equals a suffix stem SHALL NOT be published as a one-sample metric.

#### Scenario: Complete suffix group becomes one series
- **WHEN** a due Query returns numeric columns `cpu_percent_min`, `cpu_percent_max`, `cpu_percent_sum`, and `cpu_percent_count`
- **THEN** the app SHALL POST one metric `cpu_percent` whose series fields are those four values

#### Scenario: Bare column stays one sample
- **WHEN** a due Query returns only a numeric column `cpu_percent`
- **THEN** the app SHALL POST `cpu_percent` with `min` = `max` = `sum` = that value and `count` = 1

#### Scenario: Incomplete suffix group is skipped
- **WHEN** a due Query returns `cpu_percent_min` and `cpu_percent_max` and does not return `_sum` and `_count` for that stem
- **THEN** the app SHALL NOT publish `cpu_percent`

#### Scenario: Empty or non-numeric result skips push
- **WHEN** a due Query returns no rows, or a first row with only non-numeric columns
- **THEN** the app SHALL skip publication for that Query entry

#### Scenario: Query runs once per due cycle
- **WHEN** a Query entry would publish more than one numeric column
- **THEN** the app SHALL still execute the Query text once in that cycle

### Requirement: Query metrics are push-only
QUERY results SHALL be published only through the existing CustomMetrics push path. This change SHALL NOT implement SQL `CustomMetric()` for in-process gather of `custom_*` scaling metric names. Existing Azure Monitor scaling metrics SHALL keep working.

#### Scenario: Query does not implement SQL CustomMetric
- **WHEN** a scaling Metric name starts with `custom_` on a SQL resource
- **THEN** behavior SHALL remain the existing not-implemented failure; QUERY SHALL NOT satisfy that call

#### Scenario: Query publishes through CustomMetrics
- **WHEN** a SQL resource has a due Query CustomMetrics entry that returns numeric columns
- **THEN** those values SHALL be POSTed as Azure Monitor custom metrics on the configured publish ResourceId (or the resource’s own id when omitted)

### Requirement: Read-only TokenCredential SQL session
The app SHALL open the QUERY session with the same process TokenCredential used for ARM and Azure Monitor. Azure SQL Database and Azure SQL Elastic Pool Query rows SHALL require `QueryConnection.ApplicationIntent` set to `ReadOnly` or `ReadWrite`. The app SHALL NOT inject `ApplicationIntent` when the key is omitted. Operators MAY set other `QueryConnection` extras that the app merges onto the implied session. `QueryConnection` SHALL NOT accept keys that replace host, catalog, or credentials (`Server`, `Database`, `Password`, `User ID`, and the same reserved set). The app SHALL NOT parse or rewrite operator SQL. A failed QUERY SHALL skip that push group and SHALL NOT stop other CustomMetrics on the same resource. Command timeout SHALL default to 30 seconds.

#### Scenario: Azure SQL Query requires ApplicationIntent
- **WHEN** QUERY is configured on an Azure SQL Database or Elastic Pool and `QueryConnection.ApplicationIntent` is missing or not `ReadOnly`/`ReadWrite`
- **THEN** startup validation SHALL fail

#### Scenario: QueryConnection ReadOnly routes to a readable secondary
- **WHEN** a Query entry sets `QueryConnection.ApplicationIntent` to `ReadOnly`
- **THEN** the Azure SQL connection string SHALL contain `ReadOnly`

#### Scenario: QueryConnection can target the primary
- **WHEN** a Query entry sets `QueryConnection.ApplicationIntent` to `ReadWrite`
- **THEN** the Azure SQL connection string SHALL contain `ReadWrite` and SHALL NOT contain `ReadOnly`

#### Scenario: Session uses process TokenCredential
- **WHEN** QUERY authenticates to any of the four SQL types
- **THEN** it SHALL use the process TokenCredential and SHALL NOT read a SQL password from config

#### Scenario: Failed Query skips that group
- **WHEN** QUERY execution throws
- **THEN** the app SHALL log the failure, skip that Query entry’s publication, and continue other CustomMetrics

#### Scenario: Default timeout is 30 seconds
- **WHEN** a Query entry does not set a timeout
- **THEN** QUERY execution SHALL time out after 30 seconds

### Requirement: Operators own replica role zeros and log I/O omission
The app SHALL NOT filter replicas, SHALL NOT inject `replica_role` predicates, and SHALL NOT add or remove log I/O columns. Operator SQL that targets a readable secondary SHALL return zero for every published numeric column when the session is not an HA secondary (`replica_role = 1`). Operator SQL that mirrors portal DTU SHALL omit log I/O (DTU as max of CPU and data I/O only).

#### Scenario: App does not rewrite operator SQL
- **WHEN** a Query entry’s text omits `replica_role` or includes a log I/O column
- **THEN** the app SHALL execute the text as configured and SHALL NOT rewrite it

### Requirement: Operator docs state Entra user and VIEW DATABASE STATE
Operator documentation for QUERY custom metrics SHALL state that ARM `Monitoring Metrics Publisher` is not enough for the QUERY login. Docs SHALL tell operators to create the same process identity as a database user with `CREATE USER FROM EXTERNAL PROVIDER` (or the current official equivalent) and to `GRANT VIEW DATABASE STATE` for Azure SQL views such as `sys.dm_db_resource_stats`. Docs MAY include Azure SQL examples that zero on `replica_role <> 1` and compute DTU as max(cpu, data_io) without log I/O. Docs SHALL state implied catalogs, including that `sys.dm_db_resource_stats` on an Elastic Pool `master` session measures `master`, not the pool.

#### Scenario: Docs name the extra SQL grant
- **WHEN** an operator reads custom-metrics or SQL resource docs for QUERY
- **THEN** those docs SHALL include `CREATE USER FROM EXTERNAL PROVIDER` and `GRANT VIEW DATABASE STATE`

#### Scenario: Docs name implied catalogs
- **WHEN** an operator reads QUERY configuration docs
- **THEN** those docs SHALL state the four implied catalogs and SHALL warn that Elastic Pool `master` is not a user-database `sys.dm_db_resource_stats` catalog
