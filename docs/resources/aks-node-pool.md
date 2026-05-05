## AKS Node Pool

```yaml
  - Resources: 
      aks_dev_nodepools:
        ResourceId: "/subscriptions/mysubscriptionid/resourceGroups/myresourcegroup/providers/Microsoft.ContainerService/managedClusters/mycluster/agentPools/{.*}"
    Frequency: 5m
    ScalingConfigurations:
      baseline:
        CustomMetrics:
          - Name: total_vm_count
            ResourceId: "${virtualMachineScaleSetId}"
            DataExpression: "(data) => Convert.ToDouble(data.Extra[\"Vmss\"].Sku.Capacity)"
          - Name: min_node_count
            ResourceId: "${virtualMachineScaleSetId}"
            DataExpression: "(data) => Convert.ToDouble(data.Resource.Data.MinCount)"
        Metrics:
          node_cpu_usage_percentage:
            Name: Percentage CPU
            Window: 00:10
            ResourceId: "${virtualMachineScaleSetId}"
            # This is not working for windows nodes, so we need to pick up directly the VMSS
            # https://github.com/Azure/AKS/issues/5001
            #Name: node_cpu_usage_percentage
            #SplitName: nodepool
            #SplitValue: ${nodePoolName}
            #ResourceId: "/subscriptions/${subscriptionId}/resourceGroups/${resourceGroupName}/providers/Microsoft.ContainerService/managedClusters/${clusterName}"
        TimeWindow:
          Days: All
          Months: All
          StartTime: "00:00"
          EndTime: "23:59"
          TimeZone: UTC
        ScalingRules:
          autoadjust:
            # Use a metric that reflects when this rule effectively scaled (node_count)
            # to derive a per‑rule last scale time for cooldowns.
            LastScaleMetric: node_count
            ScalingStrategy: Autoadjust
            Dimension: MinNodeCount
            DimensionValueMax: "5"
            DimensionValueMin: "1"
            ScaleUpCondition: "(data) => data.Metrics[\"node_cpu_usage_percentage\"].Values.Select(i => i.Average).Take(3).Average() > 80" # Average CPU > 80% for 3 minutes
            ScaleDownCondition: "(data) => data.Metrics[\"node_cpu_usage_percentage\"].Values.Select(i => i.Average).Take(10).Average() < 60" # Average CPU < 60% for 10 minutes
            ScaleUpTarget: "(data) => data.NextDimensionValue(1)"
            ScaleDownTarget: "(data) => data.PreviousDimensionValue(1)"
            ScaleUpCooldownSeconds: 300
            ScaleDownCooldownSeconds: 1200
```

