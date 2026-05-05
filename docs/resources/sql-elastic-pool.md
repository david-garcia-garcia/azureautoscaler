## SQL Elastic Pool

### Per-database max eDTU (`PerDatabaseMaxCapacity`)

Dimension **`PerDatabaseMaxCapacity`** maps to ARM `PerDatabaseSettings.MaxCapacity`: the cap on how many eDTUs any one database in the pool may use when the pool has spare capacity. It can be scaled **independently** of pool DTU (`Dimension: Dtu`) and max data size (`Dimension: MaxDataBytes`).

Allowed targets follow Azure’s documented ladders (not the same steps as pool-level DTU tiers): **Standard** pools use one value list filtered by current pool eDTU; **Premium** pools use another list plus a **pool-size ceiling** (for example, a 1500 eDTU Premium pool cannot set per-database max above 1000).

### Default when you omit `PerDatabaseMaxCapacity`

This section describes backward-compatible behavior if your YAML never defines a scaling rule for **`PerDatabaseMaxCapacity`**.

- After each refresh, requested pool state is rebuilt from rules. Nothing sets `PerDatabaseMaxCapacity` unless a rule uses that dimension.
- **Whenever the autoscaler applies a patch to the elastic pool** and no rule has set a requested per-database max for that cycle, the patch still sends `ElasticPoolPerDatabaseSettings.MaxCapacity` equal to the pool’s **`Sku.Capacity`** in that patch (the effective pool DTU after `PreparePatch` reconciles storage/DTU). This matches the old “always tie per-database max to pool DTU” behavior.
- **If you add rules** for `PerDatabaseMaxCapacity`, the explicit requested value is written instead (snapped/clamped to Azure-valid steps and Premium ceilings).

**Operator note:** If you set per-database max manually in the portal but only scale **`Dtu`** / **`MaxDataBytes`** in config, the next autoscaler patch that changes the pool can **overwrite** `MaxCapacity` with pool DTU again, unless you also control it via **`PerDatabaseMaxCapacity`**.

```yaml
  - Resources:
      mysqldevpools_pools:
        ResourceId: "/subscriptions/mysubscriptionid/resourceGroups/myresourcegroup/providers/Microsoft.Sql/servers/mypool/elasticPools/{.*}"
      mysqlprodshared_pools:
        ResourceId: "/subscriptions/mysubscriptionid/resourceGroups/myresourcegroup/providers/Microsoft.Sql/servers/mypool2/elasticPools/{.*}"
    Frequency: 3m
    WhatIf: false
    ScalingConfigurations:
      Baseline:
        ScaleDownLockWindowMinutes: 50
        ScaleUpAllowWindowMinutes: 58
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
            ScaleUpCondition: "(data) => data.Metrics[\"dtu_consumption_percent\"].Values.Select(i => i.Average).Take(3).Average() > 80" # Average DTU > 80% for 3 minutes
            ScaleDownCondition: "(data) => data.Metrics[\"dtu_consumption_percent\"].Values.Select(i => i.Average).Take(5).Average() < 60" # Average DTU < 60% for 5 minutes
            ScaleUpTarget: "(data) => data.NextDimensionValue(1)" # You could actually specify DTU number manually, and system will find closest valid tier
            ScaleDownTarget: "(data) => data.PreviousDimensionValue(1)" # You could actually specify DTU number manually, and system will find closest valid tier
            ScaleUpCooldownSeconds: 180
            ScaleDownCoolDownSeconds: 3600
            DimensionValueMax: "200"
            DimensionValueMin: "50"
      MaxDataBytes:
        Metrics:
          storage_used:
            Name: storage_used
            Window: 00:05
            ValidValueMin: 1048576  # Reject values below 1MB - protects against broken Azure metrics returning zero
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
            # Fix target of extra 50GB or 20% additional of current storage, whatever is greater.
            # Use .Default.Value to access the primary aggregation (works regardless of aggregation type)
            ScaleTarget: "(data) => (Math.Max(data.Metrics[\"storage_used\"].Values.First().Default.Value + (50.1*1024*1024*1024), data.Metrics[\"storage_used\"].Values.First().Default.Value * 1.2)).ToString()"
            DimensionValueCeilingStep: "1"  # Round up to nearest GB
            DimensionValueMax: "1024"  # Never scale above 1024 GB
            DimensionValueMin: "1"     # Never scale below 1 GB
```

