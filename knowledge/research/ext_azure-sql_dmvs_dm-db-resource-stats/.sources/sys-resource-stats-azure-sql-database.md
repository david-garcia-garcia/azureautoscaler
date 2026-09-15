---
url: https://learn.microsoft.com/en-us/sql/relational-databases/system-catalog-views/sys-resource-stats-azure-sql-database
title: sys.resource_stats (Azure SQL Database)
fetched: 2026-09-15
authority: official
---

Returns CPU usage and storage data for a database. Collected in five-minute intervals. One row per user database per five-minute window when resource consumption changes. About 14 days of history. Includes `database_name`.

Available to user roles with permission to connect to the virtual `master` database.

You must be connected to the `master` database on the logical server to query `sys.resource_stats`.

For a more granular view, use `sys.dm_db_resource_stats` **in a user database** (15 seconds, 1 hour).
