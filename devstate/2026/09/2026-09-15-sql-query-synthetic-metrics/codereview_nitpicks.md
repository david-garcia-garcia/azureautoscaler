# Nitpicks

1. [hard] Clear conditions — `autoscaler/metrics/SqlQuerySessionFactory.cs:58` — `if plan.Engine != AzureSql` (and the matching ternary) wraps Entra user-name work; the body is PostgreSQL/MySQL, not “any non-AzureSql engine”
   → `if plan.Engine == PostgreSql || plan.Engine == MySql` (or `NeedsEntraUserName(plan.Engine)`)
   Status: done
   Argument: `NeedsEntraUserName` is `PostgreSql || MySql`.
