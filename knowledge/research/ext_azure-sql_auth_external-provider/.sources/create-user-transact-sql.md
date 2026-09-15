---
url: https://learn.microsoft.com/en-us/sql/t-sql/statements/create-user-transact-sql
title: CREATE USER (Transact-SQL)
fetched: 2026-09-15
authority: official
---

User in SQL Database or Azure Synapse based on a Microsoft Entra user: `CREATE USER [Fritz@contoso.com] FROM EXTERNAL PROVIDER;`

`FROM EXTERNAL PROVIDER` specifies that the principal is for Microsoft Entra authentication. SQL Server automatically validates the provided principal name in Microsoft Entra.

DisplayName of the Microsoft Entra object is used for groups and applications.

In SQL Database and Azure SQL Managed Instance, if the principal issuing `CREATE USER` is a service principal, the identity of the database server or managed instance must be in the Directory Readers role in Microsoft Entra.

When creating the user, if the name does not correspond to an existing Microsoft Entra login, `FROM EXTERNAL PROVIDER` creates a contained Microsoft Entra user without a login in `master`.
