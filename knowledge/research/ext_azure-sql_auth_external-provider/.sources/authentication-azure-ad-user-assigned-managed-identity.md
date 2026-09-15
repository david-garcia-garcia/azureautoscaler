---
url: https://learn.microsoft.com/en-us/azure/azure-sql/database/authentication-azure-ad-user-assigned-managed-identity
title: Managed Identity in Microsoft Entra for Azure SQL
fetched: 2026-09-15
authority: official
---

In addition to using a UMI or SMI as the instance or server identity, you can use them to access the database with `Authentication=Active Directory Managed Identity`.

You need to create a SQL user from the managed identity in the target database by using the `CREATE USER` statement.

Applies only to Azure SQL Database: an SMI as server identity can create Microsoft Entra users without Microsoft Graph permissions by using `CREATE USER` with `SID` and `TYPE` (no validation against Microsoft Entra).
