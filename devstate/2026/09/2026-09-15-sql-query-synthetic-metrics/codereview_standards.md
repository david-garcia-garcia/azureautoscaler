# Standards

1. [hard] Leave a trail — `autoscaler/metrics/CustomMetricsPusher.cs:125` — after the POST moved into `PushNamedMetricAsync`, the DataExpression debug log records YAML `Namespace` and `ResourceId` instead of the resolved dest that was POSTed
   → Log inside `PushNamedMetricAsync` with the resolved `resourceId` and `metricNamespace`
   Status: done
   Argument: success log moved into `PushNamedMetricAsync` with resolved resourceId and namespace.
2. [hard] Leave a trail — `autoscaler/metrics/CustomMetricsPusher.cs:170` — Query success log uses `metric.ResourceId` (often empty) and drops the namespace the POST already resolved
   → Same as item 1: emit the published name, namespace, and resource id from `PushNamedMetricAsync`
   Status: done
   Argument: Query path now uses the same `PushNamedMetricAsync` success log.
3. [hard] Leave a trail — `autoscaler/metrics/CustomMetricsPusher.cs:134` — Query failures collapse to the literal `"query"` while `metricIndex` and `state.AzureResourceId` are already in hand
   → Log the Query row index and resource id (and keep the exception)
   Status: done
   Argument: catch logs `query[{index}]` and `AzureResourceId`.
4. [hard] One job, one owner — `autoscaler/resources/MsSqlDatabase/MsSqlDatabaseResourceState.cs:183` — parent-server FQDN fetch is copied into Elastic Pool Refresh and rebuilds the server id from parts though `Id.Parent` already is that identity
   → One owner that calls `GetSqlServerResource` with `Id.Parent` and sets `FullyQualifiedDomainName`; both Refresh paths call it
   Status: done
   Argument: `SqlLogicalServerFullyQualifiedDomainName.ReadAsync` uses `Id.Parent`; both SQL Refresh paths call it.
5. [hard] Bound the ask — `autoscaler/metrics/SqlQueryFirstRowMapper.cs:12` — `alreadyOnFirstRow` is an unused hook; every caller lets the mapper `Read()`
   → Drop the parameter; always consume the first `Read()`
   Status: done
   Argument: dropped `alreadyOnFirstRow`; mapper always `Read()`.
