## SQL Database

### Query custom metrics

`CustomMetrics` may use `Query` instead of `DataExpression`. The session catalog is the database name in the ARM ResourceId (not `master`). Host comes from the logical server FQDN after Refresh. Auth is the process TokenCredential. There is no YAML `Database` or connection-string field.

Set `QueryConnection.ApplicationIntent` to `ReadOnly` (readable HA secondary when the tier has one) or `ReadWrite` (primary). The app does not inject a default and does not rewrite your SQL.

Two Azure permissions, two jobs:

| Need | Grant |
| --- | --- |
| Run the Query | Entra user in **each** database + `VIEW DATABASE STATE` |
| POST the custom metrics | ARM **Monitoring Metrics Publisher** on the **logical server** (or RG / subscription). A 403 `Microsoft.Insights/Metrics/write` is this role, not the SQL grant. |

```sql
CREATE USER [appName] FROM EXTERNAL PROVIDER;
GRANT VIEW DATABASE STATE TO [appName];
```

Use the Microsoft Entra display name of the managed identity or service principal.

### Example: replica series on standalone databases

Operator SQL (not a product default). Discovers standalone databases on a server, connects ReadOnly, aggregates `sys.dm_db_resource_stats` (one row every 15s) over the same window as `Frequency`, and publishes five custom metrics in the `Custom Autoscaler` namespace.

**Why `ElasticPoolId`, not `Sku.Name`.** Server list often sets `Sku.Name` to the objective (`P1`, `S2`) for both standalone and pooled databases, with `Sku.Family` null on both. Pool membership is `ElasticPoolId`. See [Resource Filter](../configuration.md#resource-filter).

**Why min/max/sum/count.** Azure Monitor stores one sample bag per POST (`Average = sum/count`). A single 15s snapshot would drop the rest of the window. Suffixes `_min` / `_max` / `_sum` / `_count` on the same stem become **one** metric named after the stem. Align `DATEADD` with `Frequency` (here 1 minute ≈ four DMV rows). One POST has a single `time`; it does not create one portal point per minute of history.

**Why `replica_*` names.** Column aliases are the Azure Monitor metric names. Prefix them so they do not collide with native `cpu_percent` / `dtu_used`.

**Why zero on `replica_role <> 1`.** `ReadOnly` does not guarantee a secondary (Basic / Standard / General Purpose have none). Role `1` is HA secondary. On primary or missing replica the published numbers are 0. For primary series, use `ReadWrite` and drop the `replica_role` filter.

**DTU.** `replica_dtu_percent` is `max(cpu, data_io)` per 15s row (no log I/O). `replica_dtu_used` is that percent times `dtu_limit`. On vCore `dtu_limit` is NULL and `replica_dtu_used` is 0.

Published metrics:

| Metric | Meaning |
| --- | --- |
| `replica_cpu_percent` | CPU % of the service-tier limit |
| `replica_data_io_percent` | Data I/O % of the limit |
| `replica_memory_percent` | Memory % of the limit |
| `replica_dtu_percent` | `max(cpu, data_io)` % |
| `replica_dtu_used` | Absolute DTU (`percent / 100 * dtu_limit`) |

```yaml
  - Resources:
      standalone_sql_replica_metrics:
        ResourceId: "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-example/providers/Microsoft.Sql/servers/sql-example/databases/{.*}"
        # Standalone only. Do not filter on Sku.Name (P1/S2 is shared with pool members).
        ResourceFilter: "(r) => r.Resource.Data.ElasticPoolId == null"
    Frequency: 5m
    WhatIf: true
    Enabled: true
    CustomMetrics:
      - Query: |
          SELECT
            CASE WHEN MIN(replica_role) = 1 THEN MIN(avg_cpu_percent) ELSE 0 END AS replica_cpu_percent_min,
            CASE WHEN MIN(replica_role) = 1 THEN MAX(avg_cpu_percent) ELSE 0 END AS replica_cpu_percent_max,
            CASE WHEN MIN(replica_role) = 1 THEN SUM(avg_cpu_percent) ELSE 0 END AS replica_cpu_percent_sum,
            COUNT_BIG(*) AS replica_cpu_percent_count,
            CASE WHEN MIN(replica_role) = 1 THEN MIN(avg_data_io_percent) ELSE 0 END AS replica_data_io_percent_min,
            CASE WHEN MIN(replica_role) = 1 THEN MAX(avg_data_io_percent) ELSE 0 END AS replica_data_io_percent_max,
            CASE WHEN MIN(replica_role) = 1 THEN SUM(avg_data_io_percent) ELSE 0 END AS replica_data_io_percent_sum,
            COUNT_BIG(*) AS replica_data_io_percent_count,
            CASE WHEN MIN(replica_role) = 1 THEN MIN(avg_memory_usage_percent) ELSE 0 END AS replica_memory_percent_min,
            CASE WHEN MIN(replica_role) = 1 THEN MAX(avg_memory_usage_percent) ELSE 0 END AS replica_memory_percent_max,
            CASE WHEN MIN(replica_role) = 1 THEN SUM(avg_memory_usage_percent) ELSE 0 END AS replica_memory_percent_sum,
            COUNT_BIG(*) AS replica_memory_percent_count,
            CASE WHEN MIN(replica_role) = 1 THEN MIN(dtu_sample) ELSE 0 END AS replica_dtu_percent_min,
            CASE WHEN MIN(replica_role) = 1 THEN MAX(dtu_sample) ELSE 0 END AS replica_dtu_percent_max,
            CASE WHEN MIN(replica_role) = 1 THEN SUM(dtu_sample) ELSE 0 END AS replica_dtu_percent_sum,
            COUNT_BIG(*) AS replica_dtu_percent_count,
            CASE WHEN MIN(replica_role) = 1 THEN MIN(dtu_used) ELSE 0 END AS replica_dtu_used_min,
            CASE WHEN MIN(replica_role) = 1 THEN MAX(dtu_used) ELSE 0 END AS replica_dtu_used_max,
            CASE WHEN MIN(replica_role) = 1 THEN SUM(dtu_used) ELSE 0 END AS replica_dtu_used_sum,
            COUNT_BIG(*) AS replica_dtu_used_count
          FROM (
            SELECT
              replica_role,
              avg_cpu_percent,
              avg_data_io_percent,
              avg_memory_usage_percent,
              dtu_sample,
              dtu_sample / 100.0 * ISNULL(dtu_limit, 0) AS dtu_used
            FROM sys.dm_db_resource_stats
            CROSS APPLY (
              SELECT MAX(v) FROM (VALUES (avg_cpu_percent), (avg_data_io_percent)) AS value(v)
            ) AS dtu(dtu_sample)
            WHERE end_time >= DATEADD(minute, -1, SYSUTCDATETIME())
          ) AS windowed
        Frequency: 1m
        QueryTimeout: 30s
        QueryConnection:
          ApplicationIntent: ReadOnly
```

Session contract and suffix mapping: [Custom metrics](../custom-metrics.md).

### DTU Scaling Example

```yaml
  - Resources:
      sqlsrvdatosetg_db:
        ResourceId: "/subscriptions/mysubscription/resourceGroups/databases/providers/Microsoft.Sql/servers/myserver/databases/*"
        # Pool membership is ElasticPoolId. Sku.Name is often P1/S2 for both standalone and pooled databases.
        ResourceFilter: "(r) => r.Resource.Data.ElasticPoolId == null && r.Resource.Data.Sku.Family == null"
    Frequency: 5m
    WhatIf: false
    ScalingConfigurations:
      Baseline:
        ScaleDownLockWindowMinutes: 50
        ScaleUpAllowWindowMinutes: 50
        Metrics:
          dtu_consumption_percent:
            Name: dtu_consumption_percent
            Window: 00:05
        TimeWindow:
          Days: All
          Months: All
          StartTime: "00:00"
          EndTime: "23:59"
          TimeZone: UTC
        ScalingRules:
          autoadjust:
            ScalingStrategy: Autoadjust
            Dimension: Dtu
            ScaleUpCondition: "(data) => data.Metrics[\"dtu_consumption_percent\"].Values.Select(i => i.Average).Take(3).Average() > 85" # Average DTU > 85% for 3 minutes
            ScaleDownCondition: "(data) => data.Metrics[\"dtu_consumption_percent\"].Values.Select(i => i.Average).Take(5).Average() < 60" # Average DTU < 60% for 5 minutes
            ScaleUpTarget: "(data) => data.NextDimensionValue(1)" # You could actually specify DTU number manually, and system will find closest valid tier
            ScaleDownTarget: "(data) => data.PreviousDimensionValue(1)" # You could actually specify DTU number manually, and system will find closest valid tier
            ScaleUpCooldownSeconds: 180
            ScaleDownCoolDownSeconds: 3600
            DimensionValueMax: "200"
            DimensionValueMin: "10"
```

### MaxDataBytes Scaling Example

```yaml
  - Resources:
      sqldb_storage_example:
        ResourceId: "/subscriptions/mysubscription/resourceGroups/databases/providers/Microsoft.Sql/servers/myserver/databases/mydb"
    Frequency: 30m
    WhatIf: false
    ScalingConfigurations:
      Baseline:
        Metrics:
          allocated_data_storage:
            Name: allocated_data_storage
            Window: 02:00:00
            TimeGrain: 01:00:00
        TimeWindow:
          Days: All
          Months: All
          StartTime: "00:00"
          EndTime: "23:59"
          TimeZone: UTC
        ScalingRules:
          fixed:
            ScalingStrategy: Fixed
            Dimension: MaxDataBytes
            # Calculate target based on metrics, but ensure it stays within bounds
            ScaleTarget: "(data) => (data.Metrics[\"allocated_data_storage\"].Values.First().Average.Value / (1024 * 1024 * 1024) + 50).ToString()"  # Current usage + 50GB
            DimensionValueCeilingStep: "1"  # Round up to nearest GB
            DimensionValueMax: "1024"  # Never scale above 1024 GB (VCore) or 250 GB (DTU Standard) - depends on SKU
            DimensionValueMin: "1"       # Never scale below 1 GB (VCore) or 0.1 GB (DTU)
```

> [!NOTE]
> The MaxDataBytes dimension supports both DTU and VCore models. Valid storage sizes depend on the database's SKU configuration:
> - **DTU Model**: ElasticPool (0.1-1024 GB), Standard (0.1-250 GB), Premium (0.1-1024 GB) - specific values only
> - **VCore Model**: General Purpose Provisioned/Serverless (1-1024 GB / 1-512 GB), Business Critical Provisioned (1-1024 GB) - natural GB increments
> - **Not Supported**: Hyperscale (any tier) and Business Critical Serverless
> 
> Even when a database belongs to an elastic pool, MaxDataBytes can be set, but pool storage limits will be enforced.

