## Azure Files

```yaml
  - Resources:
      myappfiles:
        ResourceId: "/subscriptions/mysubscription/resourceGroups/myresourcegroup/providers/Microsoft.Storage/storageAccounts/mystorageaccount/fileServices/default/shares/{^(?!apptemp$)([a-zA-Z0-9]+)$}"
    Frequency: 30m
    Enabled: true
    WhatIf: false
    ScalingConfigurations:
      Baseline:
        Metrics:
          FileCapacity:
            Name: FileCapacity
            ResourceId: "/subscriptions/${subscriptionId}/resourceGroups/${resourceGroupName}/providers/Microsoft.Storage/storageAccounts/${storageAccountName}/fileServices/default"
            Window: 02:00:00
            TimeGrain: 01:00:00 
            SplitName: "FileShare"
            SplitValue: "${fileShareName}"
            Aggregations: ["Average"]
            Transform: "(value) => value.SetAverage(value.Average / (1000 * 1000 * 1000))" # Convert to Gb
        TimeWindow:
          Days: All
          Months: All
          StartTime: "00:00"
          EndTime: "23:59"
          TimeZone: UTC
        ScalingRules:
          fixed:
            ScalingStrategy: Fixed
            Dimension: ProvisionedStorage
            # Fixed +50 GB above whatever is being used
            ScaleTarget: "(data) => (data.Metrics[\"FileCapacity\"].Values.First().Average + 50).ToString()"
```

