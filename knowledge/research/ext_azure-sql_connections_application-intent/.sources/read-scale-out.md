---
url: https://learn.microsoft.com/en-us/azure/azure-sql/database/read-scale-out
title: Read Queries on Replicas - Azure SQL Database & Azure SQL Managed Instance
fetched: 2026-09-15
authority: official
---

Premium and Business Critical single / elastic-pool databases get a primary read-write replica and one or more secondary read-only replicas. Hyperscale gets the feature when at least one secondary replica is added.

Basic, Standard, and General Purpose HA architecture does not include any replicas. Read scale-out is not available in those tiers.

Read scale-out is enabled by default on new Premium, Business Critical, and Hyperscale databases. Always enabled on Business Critical SQL Managed Instance and on Hyperscale databases with at least one secondary replica. Automatically disabled on Hyperscale databases configured with zero secondary replicas.

If the SQL connection string is configured with `ApplicationIntent=ReadOnly`, the application is redirected to a read-only replica of that database or managed instance.

For Azure SQL Database only: to ensure the application connects to the primary regardless of `ApplicationIntent`, explicitly disable read scale-out.

When read scale-out is enabled, `ApplicationIntent=ReadWrite` (default) or omitting the property directs the connection to the read-write replica. `ApplicationIntent=ReadOnly` routes to a read-only replica.

Verify with `SELECT DATABASEPROPERTYEX(DB_NAME(), 'Updateability');` — returns `READ_ONLY` when connected to a read-only replica.

In Premium and Business Critical, only one of the read-only replicas is accessible at any given time.

When connected to a read-only replica, DMVs reflect the replica. `sys.resource_stats` and `sys.elastic_pool_resource_stats` in logical `master` return primary-replica utilization.

Geo-replicated secondaries: sessions with `ApplicationIntent=ReadOnly` are routed to an HA replica; sessions without that intent are routed to the primary replica of the geo-secondary (also read-only).
