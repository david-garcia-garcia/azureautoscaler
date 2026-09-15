---
url: https://learn.microsoft.com/en-us/azure/azure-sql/database/authentication-aad-configure
title: Configure Microsoft Entra Authentication - Azure SQL Database
fetched: 2026-09-15
authority: official
---

Managing permissions for server and database principals works the same regardless of principal type (Microsoft Entra ID, SQL authentication, etc.). Recommend granting permissions to database roles, then adding users to roles.

A contained database user is not connected to a login in `master`. To create a Microsoft Entra contained database user, connect to the database with a Microsoft Entra identity that has at least `ALTER ANY USER`:

`CREATE USER [<Microsoft_Entra_principal_name>] FROM EXTERNAL PROVIDER;`

To create a contained database user for a managed identity or service principal, enter the display name of the identity:

`CREATE USER [appName] FROM EXTERNAL PROVIDER;`

Login-based users: `CREATE USER [appName] FROM LOGIN [appName];`

`CREATE USER ... FROM EXTERNAL PROVIDER` requires Azure SQL access to Microsoft Entra ID on behalf of the logged-in user.
