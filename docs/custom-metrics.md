## Custom Metrics

Custom metrics let you push additional data points from the autoscaler into Azure Monitor, so you can build dashboards/alerts on values that are not exposed as native Azure metrics (for example, total cores across all nodes in a VMSS, or memory derived from a SKU).

At the **resource configuration** level you can declare a `CustomMetrics` array:

```yaml
  - Resources:
      aks_nodepools:
        ResourceId: "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-example-aks/providers/Microsoft.ContainerService/managedClusters/aks-example/agentPools/{.*}"
    Frequency: 5m
    WhatIf: true
    Enabled: true
    CustomMetrics:
      - Name: total_core_count
        # (Optional) Namespace: overrides the global CustomMetricsNamespace when set
        # Namespace: "Custom Autoscaler"
        ResourceId: "${virtualMachineScaleSetId}"
        DataExpression: "(data) => Convert.ToDouble(data.Helpers.VmSizeToCores(data.Extra[\"Vmss\"].Sku.Name.ToString()) * Convert.ToDouble(data.Extra[\"Vmss\"].Sku.Capacity))"
        Frequency: 5m
```

### CustomMetrics configuration options

Each entry in `CustomMetrics` supports:

- **Name**: Metric name as it will appear in Azure Monitor (e.g. `total_core_count`).
- **Namespace** (optional): Custom metric namespace. If omitted, the autoscaler uses:
  - The global `CustomMetricsNamespace` at the root of `config.yml`, if set.
  - Otherwise the hardcoded default **"Custom Autoscaler"**.
- **ResourceId** (optional): ARM resource ID that receives the metric. If omitted, the current resource’s `ResourceId` is used. You can use the same replacement tokens as in [normal metric definitions](../configuration.md#metrics) (e.g. `${virtualMachineScaleSetId}`).
- **DataExpression** (required): C# expression that computes the metric value from the data context (see below). Must return a numeric value (typically `double`) that can be sent to Azure Monitor.
- **Frequency** (optional): How often to evaluate and push this custom metric. If omitted, the resource’s `Frequency` is used.

### DataExpression – available data in `data`

The `DataExpression` is executed against a strongly-typed context referred to as `data` (`CustomMetricDataContext`). The most relevant properties are:

- **`data.Resource`**: Wrapper around the target ARM resource.
  - **`data.Resource.Data`**: The full ARM resource model from the Azure SDK (e.g. `VirtualMachineScaleSetResource.Data`, `MySqlFlexibleServerResource.Data`). You can access any property that ARM exposes (SKU, capacity, tags, etc.).
- **`data.ExistingState`**: The current `ResourceState` instance for the resource (resource-type-specific object). Useful when you need autoscaler-level state rather than raw ARM.
- **`data.ResourceParts`**: Parsed parts of the ARM resource ID (subscription, resource group, name, captured regex groups, etc.). Helpful for building IDs or using regex groups from expansion.
- **`data.Helpers`** (`CustomMetricHelpers`):
  - `VmSizeToCores(string vmSize)`: Returns an `int` with the core count for a VM SKU (uses `IVmSizeResolver` when available, otherwise regex fallback).
  - `VmSizeToMemory(string vmSize)`: Returns a `long` with memory in **bytes** for a VM SKU.
  - `VmSizeToMemoryGb(string vmSize)`: Returns a `double` with memory in **GiB** for a VM SKU.

Depending on the resource type, the context may also populate `data.Extra` with additional helper objects. For example, in AKS node pool custom metrics, `data.Extra["Vmss"]` contains the underlying VMSS ARM resource.

