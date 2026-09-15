---
url: https://learn.microsoft.com/en-us/sql/relational-databases/system-dynamic-management-views/sys-dm-db-resource-stats-azure-sql-database
title: sys.dm_db_resource_stats - Azure SQL Database & SQL database in Fabric
fetched: 2026-09-15
authority: official
---

Applies to Azure SQL Database and SQL database in Microsoft Fabric. Not supported in Azure SQL Managed Instance; use `sys.server_resource_stats` there.

Returns CPU, I/O, and memory consumption for a database. One row every 15 seconds even with no activity. Historical data for approximately one hour.

Used columns:

- `end_time` — UTC end of the reporting interval.
- `avg_cpu_percent` — average compute utilization as percent of the service-tier limit.
- `avg_data_io_percent` — average data I/O utilization as percent of the service-tier limit.
- `avg_log_write_percent` — average transaction log writes (MB/s) as percent of the service-tier limit.
- `avg_memory_usage_percent` — average memory utilization as percent of the service-tier limit, including buffer-pool pages and In-Memory OLTP objects.
- `dtu_limit` — current max database DTU setting for the interval; `NULL` on vCore.
- `cpu_limit` — number of vCores for the interval; `NULL` on DTU.
- `replica_role` — 0 Primary, 1 HA secondary, 2 geo-replication forwarder, 3 named replica. Reports 1 when connected with `ReadOnly` intent to any readable secondary. Geo-secondary without `ReadOnly` reports 2. Named replica without `ReadOnly` reports 3.

Requires `VIEW DATABASE STATE`.

Data is a percentage of the max allowed limits for the running service tier / performance level.

Example heading: resource utilization for the **currently connected database**.

Official DTU percent sample: per-interval `Max` of `avg_cpu_percent`, `avg_data_io_percent`, `avg_log_write_percent` as `avg_DTU_percent`.

For a less granular, longer-retention view use `sys.resource_stats` (5 minutes, 14 days).

Elastic-pool percent values are of the max limit for the database as set in the pool configuration.
