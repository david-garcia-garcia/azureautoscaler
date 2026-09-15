---
url: https://learn.microsoft.com/en-us/azure/azure-sql/database/monitoring-with-dmvs
title: Monitor Azure SQL Database with DMVs
fetched: 2026-09-15
authority: official
---

Querying a DMV in Azure SQL Database may require `VIEW DATABASE STATE`, or `VIEW SERVER PERFORMANCE STATE`, or `VIEW SERVER SECURITY STATE`. See the article for the specific DMV.

To grant `VIEW DATABASE STATE` to a database user:

`GRANT VIEW DATABASE STATE TO [database_user];`

To grant membership in `##MS_ServerStateReader##` to a login, connect to `master` and run `ALTER SERVER ROLE [##MS_ServerStateReader##] ADD MEMBER [login_name];`.
