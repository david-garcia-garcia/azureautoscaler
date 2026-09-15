# Test coverage

1. [hard] Edge case untested — `autoscaler/metrics/SqlQuerySessionFactory.cs:78` — missing FQDN after Refresh throws skip; missing SQL Database `databaseName` at `:85` does the same; design D4 / task 2.3 named this; tests only build plans with host and catalog set (`SqlQuerySessionFactoryTests.CreateConnectionPlan_*`)
   → Assert `CreateConnectionPlan` throws when `FullyQualifiedDomainName` is unset, and when an Azure SQL Database state lacks `ResourceParts["databaseName"]`
   Status: done
   Argument: tests for missing FQDN and missing `databaseName`.
2. [hard] Critical path untested — `autoscaler/metrics/SqlQuerySessionFactory.cs:58` — PostgreSQL/MySQL Query fail-closed when the access token has no Entra user name; `ReadFirstRowAsync_UsesInjectedReaderOnce` only hits Azure SQL (skips the check); `(none)` for the OSS deny
   → Assert `ReadFirstRowAsync` on a flexible-server state throws when the token has no `preferred_username` / `upn` / `unique_name` / `appid`
   Status: done
   Argument: `ReadFirstRowAsync_PostgreSql_ThrowsWhenTokenHasNoEntraUserName`.
3. [judgement] Happy path only — `autoscaler/metrics/SqlQueryFirstRowMapper.cs:14` — empty reader returns empty; spec named no-row skip; `FirstRowMapper_ReadsOnlyFirstRow` uses two rows; `PushIfDueAsync_EmptyOrNonNumeric_DoesNotPost` uses a non-numeric row, not zero rows
   → Assert `ReadFirstRow` on an empty `DataTable` is empty
   Status: skipped
   Argument: judgement; empty-row skip already covered by pusher empty/non-numeric path.
4. [judgement] Happy path only — `autoscaler/metrics/CustomMetricsPusher.cs:88` — Query due-key `{AzureResourceId}|query|{index}` is untested; pusher tests set `FrequencyParsed = TimeSpan.Zero`, so reverting to `{id}|{Name}` would still run both Query rows
   → Assert two Query rows keep independent due clocks after one push (frequency elapsed vs not)
   Status: skipped
   Argument: judgement; due-key is specified and used; not applied unattended.
