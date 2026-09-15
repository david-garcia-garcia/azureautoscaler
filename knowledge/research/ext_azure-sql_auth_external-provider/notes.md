# CREATE USER FROM EXTERNAL PROVIDER

Official Microsoft Learn pages define how a Microsoft Entra (including managed-identity) principal becomes an Azure SQL Database user, and how `VIEW DATABASE STATE` is granted to a database user. They do not publish a single combined "managed-identity metric reader" recipe; the two facts compose. [owner](https://learn.microsoft.com/en-us/azure/azure-sql/database/authentication-aad-configure) · [extract](.sources/authentication-aad-configure.md) · [owner](https://learn.microsoft.com/en-us/azure/azure-sql/database/monitoring-with-dmvs) · [extract](.sources/monitoring-with-dmvs.md)

## Create the database user

A contained database user is a SQL user that is not mapped to a login in `master`. Create a Microsoft Entra contained user by connecting to the **target database** as a Microsoft Entra identity that has at least `ALTER ANY USER`: [owner](https://learn.microsoft.com/en-us/azure/azure-sql/database/authentication-aad-configure)

```sql
CREATE USER [<Microsoft_Entra_principal_name>] FROM EXTERNAL PROVIDER;
```

For a **managed identity or service principal**, use the **display name** of the identity: [owner](https://learn.microsoft.com/en-us/azure/azure-sql/database/authentication-aad-configure)

```sql
CREATE USER [appName] FROM EXTERNAL PROVIDER;
```

A managed identity can also sign in with `Authentication=Active Directory Managed Identity`. A SQL user for that identity must exist in the target database (`CREATE USER`). [owner](https://learn.microsoft.com/en-us/azure/azure-sql/database/authentication-azure-ad-user-assigned-managed-identity) · [extract](.sources/authentication-azure-ad-user-assigned-managed-identity.md)

`FROM EXTERNAL PROVIDER` means the principal is for Microsoft Entra authentication and the engine validates the name in Microsoft Entra. [owner](https://learn.microsoft.com/en-us/sql/t-sql/statements/create-user-transact-sql) · [extract](.sources/create-user-transact-sql.md)

If a matching Microsoft Entra **login** already exists in logical `master`, a database user can instead be mapped with `CREATE USER [appName] FROM LOGIN [appName]`. Using `FROM EXTERNAL PROVIDER` when no such login exists creates a contained user. [owner](https://learn.microsoft.com/en-us/azure/azure-sql/database/authentication-aad-configure) · [owner](https://learn.microsoft.com/en-us/sql/t-sql/statements/create-user-transact-sql)

Permission management is the same regardless of principal type (Microsoft Entra, SQL authentication, and so on). Official guidance is to grant permissions to database roles, then add users to those roles. [owner](https://learn.microsoft.com/en-us/azure/azure-sql/database/authentication-aad-configure)

## `VIEW DATABASE STATE`

`sys.dm_db_resource_stats` requires `VIEW DATABASE STATE`. [owner](https://learn.microsoft.com/en-us/sql/relational-databases/system-dynamic-management-views/sys-dm-db-resource-stats-azure-sql-database) · [extract](.sources/sys-dm-db-resource-stats-azure-sql-database.md)

Official grant to a database user (replace `database_user` with the principal name in that database): [owner](https://learn.microsoft.com/en-us/azure/azure-sql/database/monitoring-with-dmvs)

```sql
GRANT VIEW DATABASE STATE TO [database_user];
```

Official docs do **not** state a different grant syntax for managed-identity users. The grant target is the database user principal created above.

An alternative official path: a login in `##MS_ServerStateReader##` or `##MS_ServerStateManager##` receives `VIEW DATABASE STATE` on databases where a matching database user exists. [owner](https://learn.microsoft.com/en-us/azure/azure-sql/database/monitoring-with-dmvs)

## Related finding

DMV permission and scope: `ext_azure-sql_dmvs_dm-db-resource-stats/`.
