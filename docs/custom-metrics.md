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

Each entry in `CustomMetrics` supplies **exactly one** value source: `DataExpression` **or** `Query`. Do not set both. Do not omit both. There is no `Database` field, connection-string field, or SQL password field.

- **Name**: Required for `DataExpression` rows. The metric name in Azure Monitor (e.g. `total_core_count`). **Not required** for `Query` rows — each numeric column name becomes a metric name.
- **Namespace** (optional): Custom metric namespace. If omitted, the autoscaler uses:
  - The global `CustomMetricsNamespace` at the root of `config.yml`, if set.
  - Otherwise the hardcoded default **"Custom Autoscaler"**.
- **ResourceId** (optional): ARM resource ID that receives the metric. If omitted, the current resource’s `ResourceId` is used. You can use the same replacement tokens as in [normal metric definitions](../configuration.md#metrics) (e.g. `${virtualMachineScaleSetId}`).
- **DataExpression**: C# expression that computes one metric value from the data context (see below). Must return a numeric value (typically `double`). Exclusive with `Query`.
- **Query**: SQL text executed once per due cycle on Azure SQL Database, Azure SQL Elastic Pool, PostgreSQL Flexible Server, or MySQL Flexible Server. Exclusive with `DataExpression`. The first result row is read. Each **numeric** column is published as an Azure Monitor custom metric. Non-numeric columns (timestamps, strings, bools) are ignored. An empty result, or a first row with no numeric columns, skips publication for that entry.
  - A bare column (`cpu_percent`) is one sample: `min` = `max` = `sum` = value, `count` = 1.
  - Columns `stem_min`, `stem_max`, `stem_sum`, and `stem_count` become **one** metric named `stem` whose POST series uses those four fields. Azure Monitor Average is then `sum/count`. All four suffixes are required; an incomplete group is skipped. Do not also return a bare `stem` column.
- **QueryTimeout** (optional): SQL command timeout (e.g. `30s`). Defaults to **30 seconds**.
- **QueryConnection**: Extra SQL driver attributes merged onto the app-built session. Host, catalog, Encrypt/TLS, and credentials stay implied (no `Server`, `Database`, `Password`, or `User ID`). On Azure SQL Database and Elastic Pool, **`ApplicationIntent` is required**: `ReadOnly` or `ReadWrite`. PostgreSQL and MySQL do not use that key.
- **Frequency** (optional): How often to evaluate and push this custom metric. If omitted, the default is `5m`.

`Query` is rejected at startup on any non-SQL resource.

### Query sessions (SQL resources)

The app builds the session from the refreshed ARM host and an implied catalog. It authenticates with the same process TokenCredential used for ARM and Azure Monitor. On Azure SQL Database and Elastic Pool you must set `QueryConnection.ApplicationIntent` (`ReadOnly` or `ReadWrite`); the app does not inject it. There is no full connection-string field. The app does not parse or rewrite your SQL.

Implied catalogs:

| Resource | Catalog |
| --- | --- |
| Azure SQL Database | Database name already in the ARM ResourceId |
| Azure SQL Elastic Pool | `master` on the logical server |
| PostgreSQL Flexible Server | `postgres` |
| MySQL Flexible Server | `mysql`

`sys.dm_db_resource_stats` is current-database scoped. On an Elastic Pool `master` session that view measures **`master`**, not the pool. For pool-level series use `sys.elastic_pool_resource_stats` (or other operator SQL that is valid on `master`).

ARM role **Monitoring Metrics Publisher** is enough to **push** custom metrics. It is **not** enough to run `Query`. Create the same process identity as a database user and grant view permission:

```sql
CREATE USER [appName] FROM EXTERNAL PROVIDER;
GRANT VIEW DATABASE STATE TO [appName];
```

Use the Microsoft Entra display name of the managed identity or service principal. Official equivalent: `CREATE USER FROM EXTERNAL PROVIDER` as documented for contained Entra users.

`ApplicationIntent: ReadOnly` routes to a readable secondary when the tier has one. It does not guarantee a replica (Basic / Standard / General Purpose have none). `ApplicationIntent: ReadWrite` hits the primary. If you target a replica, your SQL should return zero for every published numeric column when `replica_role <> 1`. The app does not inject that filter.

Full standalone-database replica example (ResourceFilter, windowed min/max/sum/count, `replica_*` names, `replica_dtu_used`): [SQL Database — replica series](resources/sql-database.md#example-replica-series-on-standalone-databases).

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

