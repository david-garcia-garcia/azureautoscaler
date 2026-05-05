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

### Tracking per-database max eDTU as a fraction of pool capacity

A common pattern is to keep `PerDatabaseMaxCapacity` at a fixed fraction of the current pool eDTU (e.g. 80%), so that no single database can monopolize the pool while still scaling proportionally when the pool itself scales.

The recommended approach reads the `eDTU_limit` Azure Monitor metric — which always reflects the currently provisioned pool DTU — and computes the target from it. You can co-locate this rule with any existing `ScalingConfiguration` that already fetches `eDTU_limit` (such as a `MaxDataBytes` or `ForecastDtu` block) to avoid an extra metric round-trip.

**One-cycle lag:** `eDTU_limit` reflects the Azure-side value, which means it updates on the cycle *after* a pool DTU change. On the cycle when the pool scales, the [default fallback](#default-when-you-omit-perdatabasemaxcapacity) (`MaxCapacity = pool DTU`) keeps the ARM patch valid. On the next cycle the per-DB rule brings it down to the configured fraction.

```yaml
      MaxDataBytes:  # or any ScalingConfiguration that fetches eDTU_limit
        Metrics:
          allocated_data_storage:
            Name: allocated_data_storage
            Window: 00:05
            Aggregations: ["Maximum"]
            ValidValueMin: 1048576
          edtu_limit:
            Name: eDTU_limit
            Window: 00:10
            Aggregations: ["Maximum"]
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
            ScaleTarget: "(data) => (Math.Max(data.Metrics[\"allocated_data_storage\"].Values.First().Default.Value + (50.1*1024*1024*1024), data.Metrics[\"allocated_data_storage\"].Values.First().Default.Value * 1.2)).ToString()"
            DimensionValueCeilingStep: "1"
            DimensionValueMax: "1024"
            DimensionValueMin: "1"
          set_per_db_max:
            ScalingStrategy: Fixed
            Dimension: PerDatabaseMaxCapacity
            # 80% of current pool eDTU — snapped automatically to nearest valid per-DB tier.
            ScaleTarget: "(data) => (data.Metrics[\"edtu_limit\"].Values.First().Default.Value * 0.8).ToString()"
```

