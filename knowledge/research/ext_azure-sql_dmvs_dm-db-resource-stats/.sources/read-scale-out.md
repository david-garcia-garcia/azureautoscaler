---
url: https://learn.microsoft.com/en-us/azure/azure-sql/database/read-scale-out
title: Read Queries on Replicas - Azure SQL Database & Azure SQL Managed Instance
fetched: 2026-09-15
authority: official
---

When connected to a read-only replica, DMVs reflect the state of that replica. `sys.dm_db_resource_stats` is listed for last-hour CPU, data IO, and log-write utilization relative to service-objective limits.

`sys.resource_stats` and `sys.elastic_pool_resource_stats` in logical `master` return resource utilization data of the **primary** replica.
