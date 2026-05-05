## SQL Database

### DTU Scaling Example

```yaml
  - Resources:
      sqlsrvdatosetg_db:
        ResourceId: "/subscriptions/mysubscription/resourceGroups/databases/providers/Microsoft.Sql/servers/myserver/databases/*"
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

