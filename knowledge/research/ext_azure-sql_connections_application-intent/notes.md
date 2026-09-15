# ApplicationIntent ReadOnly

On Azure SQL Database, `ApplicationIntent=ReadOnly` in the SQL connection string is a routing hint: when read scale-out is enabled, the session is routed to a read-only replica. It is **not** a guarantee that a replica exists, and the session can still land on the primary. [owner](https://learn.microsoft.com/en-us/azure/azure-sql/database/read-scale-out) · [extract](.sources/read-scale-out.md) · [owner](https://learn.microsoft.com/en-us/azure/azure-sql/database/service-tier-hyperscale-replicas) · [extract](.sources/service-tier-hyperscale-replicas.md)

## What the setting does

When read scale-out is enabled for the database:

- `ApplicationIntent=ReadWrite` (the default) or omitting the property sends the connection to the read-write replica. [owner](https://learn.microsoft.com/en-us/azure/azure-sql/database/read-scale-out)
- `ApplicationIntent=ReadOnly` routes the connection to a read-only replica. [owner](https://learn.microsoft.com/en-us/azure/azure-sql/database/read-scale-out)

Sample connection string from the same page:

```
Server=tcp:<server>.database.windows.net;Database=<mydatabase>;ApplicationIntent=ReadOnly;User ID=<myLogin>;Password=<password>;Trusted_Connection=False; Encrypt=True;
```

Read scale-out is enabled by default on new Premium, Business Critical, and Hyperscale databases. It is always on for Business Critical managed instances and for Hyperscale databases with at least one secondary replica. [owner](https://learn.microsoft.com/en-us/azure/azure-sql/database/read-scale-out)

## It does not guarantee a replica

Official owners state cases where `ReadOnly` still reaches the primary, or where no replica exists:

- Hyperscale: if `ApplicationIntent` is `ReadOnly` **and the database does not have a secondary replica**, the connection is routed to the **primary** and defaults to `ReadWrite` behavior. [owner](https://learn.microsoft.com/en-us/azure/azure-sql/database/service-tier-hyperscale-replicas)
- Basic, Standard, and General Purpose High Availability architecture **does not include any replicas**. Read scale-out is not available in those tiers. [owner](https://learn.microsoft.com/en-us/azure/azure-sql/database/read-scale-out)
- Hyperscale databases configured with **zero** secondary replicas have read scale-out automatically disabled. [owner](https://learn.microsoft.com/en-us/azure/azure-sql/database/read-scale-out)
- Azure SQL Database can disable read scale-out explicitly. Then the application connects to the primary **regardless of** the `ApplicationIntent` setting. [owner](https://learn.microsoft.com/en-us/azure/azure-sql/database/read-scale-out)

Verify the landed replica with `SELECT DATABASEPROPERTYEX(DB_NAME(), 'Updateability');` — `READ_ONLY` on a read-only replica. The database context must be the user database, not `master`. [owner](https://learn.microsoft.com/en-us/azure/azure-sql/database/read-scale-out)

## Related replica facts used with the DMV

`sys.dm_db_resource_stats.replica_role` reports **1** when connected with `ReadOnly` intent to any readable secondary. See `ext_azure-sql_dmvs_dm-db-resource-stats/`. [owner](https://learn.microsoft.com/en-us/sql/relational-databases/system-dynamic-management-views/sys-dm-db-resource-stats-azure-sql-database)
