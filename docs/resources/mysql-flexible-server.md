## MySQL Flexible Server

```yaml
  - Resources:
      mysql-dev-mysql0:
        ResourceId: "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-example-dev-mysql/providers/Microsoft.DBforMySQL/flexibleServers/mysql-dev-mysql0"
    Frequency: 5m
    WhatIf: true
    Enabled: true
    # Push custom metrics for MySQL server (requires Monitoring Metrics Publisher on the server)
    CustomMetrics:
      - Name: total_core_count
        DataExpression: "(data) => Convert.ToDouble(data.Helpers.VmSizeToCores(Convert.ToString(data.Resource.Data.Sku.Name)))"
        Frequency: 5m
      - Name: total_memory_gb
        DataExpression: "(data) => Convert.ToDouble(data.Helpers.VmSizeToMemoryGb(Convert.ToString(data.Resource.Data.Sku.Name)))"
        Frequency: 5m
    ScalingConfigurations:
      Baseline:
        ScaleDownLockWindowMinutes: 50
        ScaleUpAllowWindowMinutes: 50
        Metrics:
          cpu_percent:
            Name: cpu_percent
            Window: 00:10
        TimeWindow:
          Days: All
          Months: All
          StartTime: "00:00"
          EndTime: "23:59"
          TimeZone: UTC
        ScalingRules:
          # Considering the downtime when resizing MySQL flexible server, I would not have this autoadjust rule here
          # except for development or qa environments.
          autoadjust:
            ScalingStrategy: Autoadjust
            Dimension: Sku
            ScaleUpCondition: "(data) => data.Metrics[\"cpu_percent\"].Values.Select(i => i.Average).Take(3).Average() > 85" # Average CPU > 85% for 3 minutes
            ScaleDownCondition: "(data) => data.Metrics[\"cpu_percent\"].Values.Select(i => i.Average).Take(5).Average() < 60" # Average CPU < 60% for 5 minutes
            ScaleUpTarget: "(data) => data.NextDimensionValue(1)" # You could actually specify SKU manually, and system will find closest valid tier
            ScaleDownTarget: "(data) => data.PreviousDimensionValue(1)" # You could actually specify SKU manually, and system will find closest valid tier
            ScaleUpCooldownSeconds: 180
            ScaleDownCoolDownSeconds: 3600
            DimensionValueMax: "Standard_B4ms"
            DimensionValueMin: "Standard_B1ms"
      Iops:
        Metrics:
          storage_io_count:
            Name: storage_io_count
            Window: "00:05"
            Granularity: "00:01"
            Aggregations: ["Total"]
          io_consumption_percent:
            Name: io_consumption_percent
            Window: 00:10
        TimeWindow:
          Days: All
          Months: All
          StartTime: "00:00"
          EndTime: "23:59"
          TimeZone: "Romance Standard Time"
        ScalingRules:
          autoadjust:
            ScalingStrategy: Autoadjust
            Dimension: Iops
            ScaleUpCondition: "(data) => data.Metrics[\"io_consumption_percent\"].Values.Select(i => i.Average).Take(5).Average() > 85"
            ScaleDownCondition: "(data) => data.Metrics[\"io_consumption_percent\"].Values.Select(i => i.Average).Take(5).Average() < 80"
            ScaleUpTarget: "(data) => ((data.Metrics[\"storage_io_count\"].Values.Select(i => i.Total).Take(5).Average() / 60.1) * 1.2).ToString()"
            ScaleDownTarget: "(data) => ((data.Metrics[\"storage_io_count\"].Values.Select(i => i.Total).Take(5).Average() / 60.1) * 0.8).ToString()"
            ScaleUpCooldownSeconds: 180
            ScaleDownCoolDownSeconds: 3600
            DimensionValueMax: "1500"
            DimensionValueMin: "400"
```

