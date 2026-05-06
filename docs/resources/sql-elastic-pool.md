## SQL Elastic Pool

### Per-database max eDTU (`PerDatabaseMaxCapacity`)

Dimension **`PerDatabaseMaxCapacity`** maps to ARM `PerDatabaseSettings.MaxCapacity`: the cap on how many eDTUs any one database in the pool may use when the pool has spare capacity. It can be scaled **independently** of pool DTU (`Dimension: Dtu`) and max data size (`Dimension: MaxDataBytes`).

Allowed targets follow Azure’s documented ladders (not the same steps as pool-level DTU tiers): **Standard** pools use one value list filtered by current pool eDTU; **Premium** pools use another list plus a **pool-size ceiling** (for example, a 1500 eDTU Premium pool cannot set per-database max above 1000).

> **Important — always bounded by pool DTU:** Even though `PerDatabaseMaxCapacity` is an independent scaling dimension, the autoscaler always re-clamps it against the **target** pool DTU during patch preparation before sending the ARM patch. If a `Dtu` rule and a `PerDatabaseMaxCapacity` rule both fire in the same cycle and the pool is being scaled down, the per-database max will be silently reduced to the nearest valid tier at or below the new pool DTU ceiling. This is required because Azure rejects any patch where `PerDatabaseSettings.MaxCapacity` exceeds the pool's new eDTU capacity. Design your `PerDatabaseMaxCapacity` rules with this in mind: the effective value applied may be lower than the rule's target whenever a simultaneous pool DTU downscale occurs.

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

### Preventing over-downscaling with a current-usage floor rule

Forecast-based rules can predict a **low** DTU target when they expect quiet periods. If **actual** demand is still high (for example, a spike the model has not caught up to yet), that low target might otherwise win and **downscale the pool too aggressively**. The autoscaler evaluates every active rule in the same cycle and then reconciles dimensions; for pool **DTU**, the **effective target is the maximum** of the values requested by the rules that fire. You can exploit that by adding a **Fixed** rule whose `ScaleTarget` tracks **current usage** (`eDTU_used` with a **Maximum** aggregation over a short window). That value acts as a **floor**: the pool will not scale **below** roughly the observed consumption tier, while **scale-up** from a forecast or utilization rule is unchanged because those targets are usually higher.

The example below adds a `current_usage_floor` rule beside a hypothetical `ForecastDtu` block in the same `ScalingConfiguration`. Adjust metric names, windows, and `DimensionValueMin` / `DimensionValueMax` to match your environment and supported DTU tiers.

```yaml
    ScalingConfigurations:
      ForecastDtu:
        ScaleDownLockWindowMinutes: 50
        ScaleUpAllowWindowMinutes: 58
        TransientErrors:
          ElasticPoolUpdateLinksNotInCatchup: 10
          ElasticPoolBusy: 5
        Metrics:
          edtu_used:
            Name: eDTU_used
            Window: "00:15"
            Aggregations: ["Maximum"]
          forecast_metric:
            Name: dtu_consumption_percent   # example; replace with your forecast inputs
            Window: "00:05"
        TimeWindow:
          Days: All
          Months: All
          StartTime: "00:00"
          EndTime: "23:59"
          TimeZone: UTC
        ScalingRules:
          forecast_rule:
            ScalingStrategy: Fixed        # placeholder — use your real ForecastDtu / Autoadjust rule IDs here
            Dimension: Dtu
            ScaleTarget: "(data) => \"100\""   # replace with your forecast-driven expression
            DimensionValueMin: "50"
            DimensionValueMax: "400"
          current_usage_floor:
            ScalingStrategy: Fixed
            Dimension: Dtu
            ScaleTarget: "(data) => data.Metrics[\"edtu_used\"].Values.First().Maximum.Value.ToString()"
            DimensionValueMin: "50"
            DimensionValueMax: "400"
```

Because targets are combined with a **maximum** across rules, the `current_usage_floor` rule does not block scale-up when another rule wants a higher DTU.

### Billing-hour windows: `ScaleDownLockWindowMinutes` and `ScaleUpAllowWindowMinutes`

These settings gate scale actions by the **UTC minute** within each clock hour (billing-hour protection).

- **`ScaleDownLockWindowMinutes` = N**  
  Scale-**down** is **not run** while the current UTC minute is **0 through N-1** (the **first N minutes** of the hour). Scale-down is **allowed** from minute **N** through **59**.  
  *Example:* N = **50** — scale-down is locked during minutes **0–49** and allowed during **50–59**.

- **`ScaleUpAllowWindowMinutes` = N**  
  Scale-**up** is **not run** while the current UTC minute is **N through 59**. Scale-up is **allowed** from minute **0** through **N-1**.  
  *Example:* N = **58** — scale-up is allowed during **0–57** and blocked at **58–59**.

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

