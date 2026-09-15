---
url: https://learn.microsoft.com/en-us/azure/azure-sql/database/monitoring-with-dmvs
title: Monitor Azure SQL Database with DMVs
fetched: 2026-09-15
authority: official
---

Use `sys.dm_db_resource_stats` first for current-state analysis. Sample query aliases `DB_NAME()` as `database_name` and aggregates CPU, data IO, log write, memory, and workers for **the current database** over the past hour.

The view records CPU, data I/O, log writes, worker threads, and memory usage toward the compute-size limit every 15 seconds for about one hour.

`sys.resource_stats` lives in the `master` database: 5-minute grain, about 14 days. You must be connected to `master` to query it. It exposes consumed resource information for each active database on the logical server.
