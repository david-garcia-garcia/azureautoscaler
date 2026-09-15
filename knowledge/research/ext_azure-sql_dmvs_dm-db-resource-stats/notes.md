# sys.dm_db_resource_stats

Azure SQL Database DMV that returns CPU, I/O, and memory consumption for **the currently connected database**. It is not a server-wide catalog. [owner](https://learn.microsoft.com/en-us/sql/relational-databases/system-dynamic-management-views/sys-dm-db-resource-stats-azure-sql-database) · [extract](.sources/sys-dm-db-resource-stats-azure-sql-database.md)

Not supported on Azure SQL Managed Instance. Use `sys.server_resource_stats` there. [owner](https://learn.microsoft.com/en-us/sql/relational-databases/system-dynamic-management-views/sys-dm-db-resource-stats-azure-sql-database)

## Scope

Current-database scoped.

- Official samples describe the result as resource use for **the currently connected database** / **the current database**. [owner](https://learn.microsoft.com/en-us/sql/relational-databases/system-dynamic-management-views/sys-dm-db-resource-stats-azure-sql-database) · [owner](https://learn.microsoft.com/en-us/azure/azure-sql/database/monitoring-with-dmvs)
- Official guidance is to **connect directly to the user database** to query this view. [owner](https://learn.microsoft.com/en-us/azure/azure-sql/database/troubleshoot-common-errors-issues) · [extract](.sources/troubleshoot-common-errors-issues.md)
- The same family of docs place the multi-database, longer-retention catalog on logical `master` as `sys.resource_stats`, and say to use `sys.dm_db_resource_stats` **in a user database**. [owner](https://learn.microsoft.com/en-us/sql/relational-databases/system-catalog-views/sys-resource-stats-azure-sql-database) · [extract](.sources/sys-resource-stats-azure-sql-database.md) · [owner](https://learn.microsoft.com/en-us/azure/azure-sql/database/monitoring-with-dmvs)

## Querying while connected to `master`

Official docs do **not** document this DMV as a way to measure user databases from `master`.

- `sys.resource_stats` (queried while connected to logical `master`) is the view that returns per-user-database rows (`database_name`) for ~14 days at 5-minute grain. [owner](https://learn.microsoft.com/en-us/sql/relational-databases/system-catalog-views/sys-resource-stats-azure-sql-database)
- `sys.resource_stats` and `sys.elastic_pool_resource_stats` in logical `master` return resource utilization of the **primary** replica. [owner](https://learn.microsoft.com/en-us/azure/azure-sql/database/read-scale-out) · [extract](.sources/read-scale-out.md)

Authority `inference`: because the DMV is defined as the currently connected database, a session whose database context is `master` would see that connected database, not a catalog of user databases. Official pages never publish a `master`-context result set for this DMV.

## Columns (portal-style CPU / data IO / memory / DTU / log IO)

Percent values are of the service-tier / performance-level limit. One row every 15 seconds; about one hour of history. [owner](https://learn.microsoft.com/en-us/sql/relational-databases/system-dynamic-management-views/sys-dm-db-resource-stats-azure-sql-database)

| Column | Official meaning |
| --- | --- |
| `avg_cpu_percent` | Average compute utilization as percent of the service-tier limit. |
| `avg_data_io_percent` | Average data I/O utilization as percent of the service-tier limit. |
| `avg_log_write_percent` | Average transaction log writes (MB/s) as percent of the service-tier limit. |
| `avg_memory_usage_percent` | Average memory utilization as percent of the service-tier limit, including buffer-pool pages and In-Memory OLTP storage. |
| `dtu_limit` | Current max database DTU setting for the interval. `NULL` on vCore. |
| `cpu_limit` | Number of vCores for the interval. `NULL` on DTU. |

Official DTU-percentage sample for the user database over the past hour is the per-interval maximum of `avg_cpu_percent`, `avg_data_io_percent`, and `avg_log_write_percent` (not memory): [owner](https://learn.microsoft.com/en-us/sql/relational-databases/system-dynamic-management-views/sys-dm-db-resource-stats-azure-sql-database)

```sql
SELECT end_time,
  (SELECT Max(v)
   FROM (VALUES (avg_cpu_percent), (avg_data_io_percent), (avg_log_write_percent)) AS
   value(v)) AS [avg_DTU_percent]
FROM sys.dm_db_resource_stats;
```

Elastic-pool members: percent values are of the max limit set for the database in the pool configuration. [owner](https://learn.microsoft.com/en-us/sql/relational-databases/system-dynamic-management-views/sys-dm-db-resource-stats-azure-sql-database)

## `replica_role`

`int`. Current replica role: [owner](https://learn.microsoft.com/en-us/sql/relational-databases/system-dynamic-management-views/sys-dm-db-resource-stats-azure-sql-database)

| Value | Role |
| --- | --- |
| 0 | Primary |
| 1 | High availability (HA) secondary |
| 2 | Geo-replication forwarder |
| 3 | Named replica |

Reports **1** when connected with `ReadOnly` intent to **any readable secondary**. Connecting to a geo-secondary without `ReadOnly` intent reports **2**. Connecting to a named replica without `ReadOnly` intent reports **3**. [owner](https://learn.microsoft.com/en-us/sql/relational-databases/system-dynamic-management-views/sys-dm-db-resource-stats-azure-sql-database)

When connected to a read-only replica, DMVs (including this one) reflect **that replica**. [owner](https://learn.microsoft.com/en-us/azure/azure-sql/database/read-scale-out)

## Permissions

Requires `VIEW DATABASE STATE`. [owner](https://learn.microsoft.com/en-us/sql/relational-databases/system-dynamic-management-views/sys-dm-db-resource-stats-azure-sql-database)

## Related finding

How `ApplicationIntent=ReadOnly` chooses a replica: `ext_azure-sql_connections_application-intent/`.
