## PostgreSQL Flexible Server

`CustomMetrics` may use `Query` instead of `DataExpression`. The session catalog is `postgres`. Host is the flexible-server FQDN after Refresh. Auth is the process TokenCredential (scope `https://ossrdbms-aad.database.windows.net/.default`). ARM `Monitoring Metrics Publisher` does not grant SQL access — create the same identity in PostgreSQL (`CREATE USER` / Entra) and grant the rights your SQL needs. For Azure SQL-style docs the equivalent grant text is `CREATE USER [appName] FROM EXTERNAL PROVIDER` and `GRANT VIEW DATABASE STATE TO [appName]`.

```yaml
  - Resources:
      postgresql-dev-db0:
        ResourceId: "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-example-dev-postgresql/providers/Microsoft.DBforPostgreSQL/flexibleServers/postgresql-dev-db0"
    Frequency: 5m
    WhatIf: true
    Enabled: true
    CustomMetrics:
      - Name: total_core_count
        DataExpression: "(data) => Convert.ToDouble(data.Helpers.VmSizeToCores(Convert.ToString(data.Resource.Data.Sku.Name)))"
        Frequency: 5m
      - Name: total_memory_gb
        DataExpression: "(data) => Convert.ToDouble(data.Helpers.VmSizeToMemoryGb(Convert.ToString(data.Resource.Data.Sku.Name)))"
        Frequency: 5m
      # Query is XOR with DataExpression. Catalog is postgres. No Name required.
      # CREATE USER FROM EXTERNAL PROVIDER + GRANT VIEW DATABASE STATE (or PG equivalent) on the identity.
      - Query: |
          SELECT 1::float AS cpu_percent
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
          autoadjust:
            ScalingStrategy: Autoadjust
            Dimension: Sku
            ScaleUpCondition: "(data) => data.Metrics[\"cpu_percent\"].Values.Select(i => i.Default).Take(3).Average() > 85"
            ScaleDownCondition: "(data) => data.Metrics[\"cpu_percent\"].Values.Select(i => i.Default).Take(5).Average() < 60"
            ScaleUpTarget: "(data) => data.NextDimensionValue(1)"
            ScaleDownTarget: "(data) => data.PreviousDimensionValue(1)"
            ScaleUpCooldownSeconds: 180
            ScaleDownCoolDownSeconds: 3600
            DimensionValueMax: "Standard_B4ms"
            DimensionValueMin: "Standard_B1ms"
```

