---
url: https://learn.microsoft.com/en-us/azure/azure-sql/database/service-tier-hyperscale-replicas
title: Hyperscale secondary replicas - Azure SQL Database
fetched: 2026-09-15
authority: official
---

In Hyperscale, the `ApplicationIntent` argument dictates whether the connection is routed to the read-write primary or to a read-only HA replica.

If `ApplicationIntent` is set to `ReadOnly` and the database does not have a secondary replica, the connection is routed to the primary replica and defaults to the `ReadWrite` behavior.

There can be zero to four HA replicas.

All HA replicas are identical in resource capacity. With more than one HA replica, read-intent workload is distributed arbitrarily across available HA replicas.
