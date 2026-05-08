## The configuration file

### Configuration lifecycle

The application parses, validates, and loads the configuration once during process startup. Any change to `config.yml` or to files referenced via `$include` requires a process restart (YAML is loaded through an in-memory stream, so there is no file-watcher–based reload).

## Global structure

The configuration file is a yaml object with the properties:

```yaml
DefaultResourceWhatIf: true # Default whatif for resources if not set
DefaultResourceEnabled: true # Default enabled for resources if not set
DefaultResourceFrequency: 10m # Default resource refresh frequency for resource if not set
# Uses .Net 8 logging configuration
# see https://learn.microsoft.com/en-us/dotnet/core/extensions/console-log-formatter#set-formatter-with-configuration
Logging:
  LogLevel:
    Default: "Trace"
    Microsoft.Hosting.Lifetime: "Trace"
  Console:
    FormatterName: "simple"
    FormatterOptions:
      SingleLine: true
      TimestampFormat: "HH:mm:ss"
Resources:
  - R0
  - R1
  - R2
```

### Composing `Resources` with `$include`

You can split a large configuration into smaller YAML files and pull their resource entries into the main `Resources` list. Add list items of the form `- $include: <relative-path>` anywhere in the top-level `Resources` sequence. Paths are resolved relative to the directory that contains the main `config.yml`, not the process working directory. Inline resource entries and `$include` entries can be mixed; each `$include` is expanded in order, so the final list matches declaration order.

**Main `config.yml` example**

```yaml
Resources:
  - $include: resources/aks.yaml
  - $include: resources/sql.yaml
  - Resources:
      my_inline_resource:
        ResourceId: "/subscriptions/.../resourceGroups/.../providers/..."
    Frequency: 5m
```

**Included file format**

Each included file must be a YAML **sequence at the root** (a list), with one element per resource entry—the same shape as each item you would put directly under `Resources` in the main file:

```yaml
# resources/aks.yaml
- Resources:
    aks_dev:
      ResourceId: "/subscriptions/.../providers/Microsoft.ContainerService/managedClusters/..."
  Frequency: 5m
  Enabled: true
```

**Limitations**

- **Only the main config is scanned** for `$include`. The loader does not expand `$include` inside included files; if an included file contains a `$include` entry, it is treated as a normal nested object and will typically fail validation or binding.
- **No `reloadOnChange` for included files**: configuration is read through an in-memory YAML stream. Editing an included file does not trigger a reload; restart the process (or rely on your platform’s restart policy) after changes.

At startup, each successfully loaded include path is written at **Information** log level (category `ConfigurationIncludes`) so you can trace which files were merged.

### Resource-Specific Logging

Each resource has its own logging category, allowing you to set different log levels for individual resources. This is useful for debugging specific resources without increasing verbosity for all resources.

**Logging Category Format:**
- **Non-expanded resources**: The category is the resource key used in the YAML configuration.
- **Expanded resources**: The category is `{resource_key}_{expanded_resource_name}`, where:
  - `resource_key` is the key from your YAML configuration
  - `expanded_resource_name` is the name of the discovered resource (e.g., node pool name, file share name, database name)

**Example:**
```yaml
Resources:
  - Resources:
      aks_dev_nodepools:  # This is the resource_key
        ResourceId: "/subscriptions/.../agentPools/{.*}"  # Expanded resource
```

When resources are discovered, you'll see log messages like:
```
autoscaler[0] Adding new resource aks_dev_nodepools_default: /subscriptions/.../agentPools/default
autoscaler[0] Adding new resource aks_dev_nodepools_w25p1: /subscriptions/.../agentPools/w25p1
```

In this example:
- `aks_dev_nodepools_default` is the logging category for the "default" node pool
- `aks_dev_nodepools_w25p1` is the logging category for the "w25p1" node pool

**Setting Log Levels Per Resource:**
```yaml
Logging:
  LogLevel:
    Default: "Information"
    Microsoft.Hosting.Lifetime: "Information"
    aks_dev_nodepools_default: "Debug"      # Debug level for default node pool
    aks_dev_nodepools_w25p1: "Trace"        # Trace level for w25p1 node pool
    mysqldevpools_pool1: "Warning"       # Warning level for a specific SQL pool
```

**Using Wildcards for Expanded Resources:**
You can use wildcards (`*`) in log category names to target all expanded resources from a single resource key. This is particularly useful when you have many auto-discovered resources and want to set the same log level for all of them.

```yaml
Logging:
  LogLevel:
    Default: "Information"
    aks_dev_nodepools_*: "Debug"            # Debug level for all expanded node pools
    mysqldevpools_*: "Trace"              # Trace level for all expanded SQL pools
    myappfiles_*: "Warning"        # Warning level for all expanded file shares
```

The wildcard matches any expanded resource name, so `aks_dev_nodepools_*` will match `aks_dev_nodepools_default`, `aks_dev_nodepools_w25p1`, and any other node pools discovered from the `aks_dev_nodepools` resource key.

## Resource structure

### Scaling configurations

The **`TimeWindow`** block is optional. When omitted, the autoscaler treats the scaling configuration as active **all day, every day, in UTC** (equivalent to `Days: All`, `Months: All`, `StartTime: "00:00"`, `EndTime: "23:59"`, `TimeZone: UTC`).

Minimal example with no `TimeWindow` (always evaluated whenever the resource runs):

```yaml
    ScalingConfigurations:
      AlwaysScale:
        ScalingRules:
          fixed:
            ScalingStrategy: Fixed
            Dimension: Dtu
            ScaleTarget: "(data) => (100).ToString()"
```

In this simple example, we will be scaling two Azure Sql Elastic Pools so that they will have 50 DTU during working hours, 20 DTU during the night, and 10 DTU during weekends.

```yaml
  - Resources:
      mysqldevpools_dev_mypooldev:
        ResourceId: "/subscriptions/mysubscription/resourceGroups/mysourcegroup/providers/Microsoft.Sql/servers/sql0/elasticPools/pool1"
      mysqldevpools_dev_mypooldev2:
        ResourceId: "/subscriptions/mysubscription/resourceGroups/mysourcegroup/providers/Microsoft.Sql/servers/sql0/elasticPools/pool2"
    Frequency: 5m
    WhatIf: true
    Enabled: true
    ScalingConfigurations:
      Nightly:
        TimeWindow:
          Days: ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday"]
          Months: All
          StartTime: "21:00"
          EndTime: "05:00"
          TimeZone: Romance Standard Time
        ScalingRules:
          fixed:
            ScalingStrategy: Fixed
            Dimension: Dtu
            ScaleTarget: "(data) => (50).ToString()"
      Daily:
        TimeWindow:
          Days: ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday"]
          Months: All
          StartTime: "05:00"
          EndTime: "20:59"
          TimeZone: Romance Standard Time
        ScalingRules:
          fixed:
            ScalingStrategy: Fixed
            Dimension: Dtu
            ScaleTarget: "(data) => (20).ToString()"
      Weekend:
        TimeWindow:
          Days: ["Saturday", "Sunday"]
          Months: All
          StartTime: "00:00"
          EndTime: "23:59"
          TimeZone: Romance Standard Time
        ScalingRules:
          fixed:
            ScalingStrategy: Fixed
            Dimension: Dtu
            ScaleTarget: "(data) => (10).ToString()"
      # Storage scaling: keep provisioned storage ahead of actual usage
      MaxDataBytes:
        Metrics:
          storage_allocated:
            Name: allocated_data_storage
            Window: 00:05
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
            ScaleTarget: "(data) => (Math.Max(data.Metrics[\"storage_allocated\"].Values.First().Average.Value + (50.1*1024*1024*1024), data.Metrics[\"storage_allocated\"].Values.First().Average.Value * 1.2)).ToString()"
```

As you can see in the previous example, a group of resources share a Resource Configuration, which consists of:

* **A map of Resources**: this is a list of all the resources in Azure this configuration will be operating on. They must all be of the same type.
* **Frequency**: how often should this resource state should be evaluated.
* **WhatIf**: you can set this to true prevent the application from actually manipulating the resources. This will let you see what the application would have done by analyzing the logs and ensure you are comfortable with the scaling configuration.
* **Enabled**: effectively disables the Resource Configuration
* **ScalingConfigurations**: A map containing each one of the scaling configurations that will be evaluated for the resource, it is important to note that:
  * All scaling configurations are evaluated according to the **TimeWindow**. Multiple configurations can overlap without issues.
  * When multiple configurations overlap, and they provide different indications on scale dimension targets, the application will always utilize the **greatest** one.

### Resource Expansion

You can use resource expansion for the application to automatically discover all child resources of a given parent resource.

For elasticpoools:

```yaml
  - Resources:
      mysqldevpools:
        ResourceId: "/subscriptions/mysubscription/resourceGroups/myresourcegroup/providers/Microsoft.Sql/servers/sql0/elasticPools/{.*}"
```

If you want to target a specific subset of resource, you can use a regular expression:

```yaml
  - Resources:
      mysqldevpools:
        ResourceId: "/subscriptions/mysubscription/resourceGroups/myresourcegroup/providers/Microsoft.Sql/servers/sql0/elasticPools/{^pool-}"
```

Resource expansion works for:

* File shares in a storage account
* Node pools in an AKS cluster
* SQL Databases in an Azure SQL Server
* SQL Elastic Pools in an Azure SQL Server

### Resource Filter

When using wildcard expansion, you can narrow down the discovered resources using `ResourceFilter` — an optional C# lambda expression compiled at startup. Only resources for which the expression returns `true` are added to the autoscaler's resource set. Resources with a literal (non-wildcard) `ResourceId` are never filtered.

The lambda receives a `ResourceFilterContext` parameter with the following properties:

| Property | Type | Description |
|---|---|---|
| `ResourceName` | `string` | The name of the expanded resource (database name, pool name, etc.) |
| `Tags` | `IDictionary<string, string>` | Azure tags on the resource at expansion time |
| `Resource` | `object` | The raw ARM resource object — Dynamic LINQ resolves members against the actual runtime type at runtime |

Access resource-type-specific properties directly via `r.Resource` — the same pattern already used in `DataExpression` for custom metrics (e.g. `data.Resource.Data.Sku.Name`).

#### Azure SQL Database SKU reference

When filtering SQL databases expanded from a `databases/*` wildcard, the `Sku.Name` value identifies the database model:

| `Sku.Name` | Model | `Sku.Family` | Notes |
|---|---|---|---|
| `Basic` | DTU | `null` | 5 DTU fixed |
| `Standard` | DTU | `null` | S0–S9 |
| `Premium` | DTU | `null` | P1–P15 |
| `ElasticPool` | DTU (pooled) | `null` | Member of an elastic pool — uses pool-level metrics, not per-database DTU |
| `GeneralPurpose` | vCore | non-null (e.g. `Gen5`) | GP_Gen5_2, GP_Fsv2_8, GP_DC_2, … |
| `BusinessCritical` | vCore | non-null | BC_Gen5_4, … |
| `Hyperscale` | vCore | non-null | HS_Gen5_4, … |

**Example — standalone DTU SQL databases only** (`Basic`, `Standard`, `Premium`; excludes elastic-pool members and vCore):

```yaml
  - Resources:
      sql_dtu_databases:
        ResourceId: "/subscriptions/.../servers/mysqlserver/databases/*"
        ResourceFilter: "(r) => r.Resource.Data.Sku.Name == \"Basic\" || r.Resource.Data.Sku.Name == \"Standard\" || r.Resource.Data.Sku.Name == \"Premium\""
```

**Example — vCore databases only** (excludes all DTU tiers and elastic-pool members):

```yaml
  - Resources:
      sql_vcore_databases:
        ResourceId: "/subscriptions/.../servers/mysqlserver/databases/*"
        ResourceFilter: "(r) => r.Resource.Data.Sku.Family != null"
```

**Example — tag-based filtering** (only databases tagged `env=prod`):

```yaml
  - Resources:
      sql_prod_databases:
        ResourceId: "/subscriptions/.../servers/mysqlserver/databases/*"
        ResourceFilter: "(r) => r.Tags.ContainsKey(\"env\") && r.Tags[\"env\"] == \"prod\""
```

After expansion the autoscaler logs a summary at `Information` level for each resource instance with a filter set:

```
ResourceFilter 'sql_dtu_databases': 8 discovered, 3 filtered out, 5 added.
```

> **Note:** If the expression fails to compile (e.g. references a non-existent property), startup fails immediately with a descriptive error. Invalid filters are never silently ignored.

### Resource Tags

Azure Autoscaler reads tags from resources and makes them available in the `ResourceTags` dictionary for each resource. Tags can be used to control resource behavior.

For most Azure resources (SQL Databases, SQL Elastic Pools, MySQL Flexible Servers, PostgreSQL Flexible Servers), tags are read directly from the resource.

**Edge Cases:**

- **AKS Node Pools**: Node pools don't support Azure resource tags. Tags must be set as **nodeLabels** on the node pool, which are automatically included in `ResourceTags`.

- **File Shares**: File shares don't support Azure resource tags. Tags must be set on the **parent storage account** with the format `{fileShareName}:{tagKey}`. The prefix is automatically removed when added to `ResourceTags`. For example, a storage account tag `myshare:autoscaler.enabled=true` will appear as `autoscaler.enabled=true` in the `myshare` file share's `ResourceTags`.

**Implemented Tags:**

- **`autoscaler.disabled=true`**: Disables autoscaling for the resource indefinitely. When set, the resource will not be evaluated or scaled until the tag is removed or set to a different value. This is useful for temporarily disabling autoscaling on specific resources without removing them from the configuration.

### Metrics

Because you need to make real time decisions based on resource metrics, each Scaling Configuration can declare a set of metrics that will be evaluated on the resource and made available for usage in the scaling rules.

```yaml
  - Resources:
      mysqldevpools_dev_mypooldev:
        ResourceId: "/subscriptions/xx/resourceGroups/rg/providers/Microsoft.Storage/storageAccounts/account/fileServices/default/shares/share"
    ScalingConfigurations:
      Baseline:
        Metrics:
          FileCapacity:
            # Name is the name of the metric in Azure Metrics
            Name: FileCapacity
            # (OPTIONAL) Namespace. When reading metrics from a custom namespace (for example, custom
            # metrics that you previously pushed via CustomMetrics), set the Azure Monitor metric
            # namespace here (e.g. "Custom Autoscaler"). When omitted, the default Azure namespace
            # for the resource type is used.
            # Namespace: "Custom Autoscaler"
            # (OPTIONAL) resourceID indicates what resource to get the metric from. Sometimes the metrics for some resource actually belong to the parent resource, and are accessed through the usage of splits. If not specified, the actual ID of the configured resource will be used.
            ResourceId: "/subscriptions/${subscriptionId}/resourceGroups/${resourceGroupName}/providers/Microsoft.Storage/storageAccounts/${storageAccountName}/fileServices/default"
            # Evaluation window. Metric evaluation will retrieve data from (Now - Window) to Now
            Window: 02:00:00
            # TimeGrain, as defined in the Azure Metrics API
            TimeGrain: 01:00:00 
            # (OPTIONAL) SplitName. You can use resource replace groups in the splitname.
            SplitName: "FileShare"
            # (OPTIONAL) SplitValue. You can use resource replace groups in the splitname.
            SplitValue: "apptemp"
            # (OPTIONAL) Aggregations. What aggregations to be retrieved for the metric. If not specified defaults
            # to "Average". Careful with this because the default Average is not the default behaviour for all metrics
            # in the portal, where some resource have different aggregations set as default.
            # The primary aggregation will be available in the Default property for use in scaling rules.
            Aggregations: ["Total"]
            # (OPTIONAL) Manipulate the individual metric values before sending them to evaluation 
            Transform: "(value) => value / (1000 * 1000 * 1000)" # Convert to Gb
            # (OPTIONAL) AllowFail. When set to true, allows the metric to fail to load without throwing an exception.
            # Instead, a debug message will be logged and the metric will be skipped. Defaults to false.
            # Useful when a metric may not be available for certain resource configurations.
            # For example, dtu_consumption_percent is not available for VCore model SQL databases, only for DTU model.
            AllowFail: false
            # (OPTIONAL) ValidValueMin. Minimum valid value for this metric (inclusive). If any data point in 
            # the metric returns a value below this threshold, the entire metric will be marked as invalid 
            # and scaling rules using this metric will be skipped. Values equal to ValidValueMin are 
            # considered valid. Useful for detecting broken Azure metrics that sometimes return zero or 
            # null values. For example, storage_used should never be 0 for a pool with actual data.
            ValidValueMin: 1048576  # Reject values < 1MB (1MB itself is valid)
            # (OPTIONAL) ValidValueMax. Maximum valid value for this metric (inclusive). If any data point 
            # in the metric returns a value above this threshold, the entire metric will be marked as 
            # invalid and scaling rules using this metric will be skipped. Values equal to ValidValueMax 
            # are considered valid. Useful for detecting anomalous metric values.
            ValidValueMax: 1099511627776  # Reject values > 1TB (1TB itself is valid)
          storage_used:
            Name: storage_used
            Window: 00:05
            ValidValueMin: 1048576  # Reject values below 1MB
          dtu_consumption_percent:
            Name: dtu_consumption_percent
            Window: 00:05
            # This metric is only available for DTU model databases, not VCore model
            AllowFail: true
```

The available metrics depend on the type of resources:

* [Supported metrics - Microsoft.FileShares/fileShares - Azure Monitor | Microsoft Learn](https://learn.microsoft.com/en-us/azure/azure-monitor/reference/supported-metrics/microsoft-fileshares-fileshares-metrics)
* [Supported metrics - Microsoft.Compute/virtualmachineScaleSets - Azure Monitor | Microsoft Learn](https://learn.microsoft.com/en-us/azure/azure-monitor/reference/supported-metrics/microsoft-compute-virtualmachinescalesets-metrics)
* [Supported metrics - Microsoft.Sql/servers/elasticpools - Azure Monitor | Microsoft Learn](https://learn.microsoft.com/en-us/azure/azure-monitor/reference/supported-metrics/microsoft-sql-servers-elasticpools-metrics)
* [Monitoring data reference for Azure Kubernetes Service - Azure Kubernetes Service | Microsoft Learn](https://learn.microsoft.com/en-us/azure/aks/monitor-aks-reference)

You can query metrics of resources different to the one you are scaling. I.e. if you are scaling a Windows node pool in AKS, you will need to retrieve metrics from the underlying VMSS. In those cases, the VMSS is available as a replacement token (see the [AKS Node Pool](resources/aks-node-pool.md) example).

### Using the Default Property in Rules

Each metric data point has a `Default` property that automatically contains the primary aggregation value (Average, Maximum, Minimum, Total, or Count). This allows you to write scaling rules that work regardless of which aggregation you configure.

**Best Practice:** Use `.Default.Value` in your scaling rules instead of `.Average.Value`, `.Maximum.Value`, etc. This way, you can change the aggregation type in the metric configuration without having to update your scaling rules.

**Example:**

```yaml
Metrics:
  storage_used:
    Name: storage_used
    Window: 00:05
    Aggregations: ["Maximum"]  # Can change to Average, Minimum, etc.

ScalingRules:
  fixed:
    ScalingStrategy: Fixed
    Dimension: MaxDataBytes
    # This rule works regardless of which aggregation is configured above
    ScaleTarget: "(data) => (data.Metrics[\"storage_used\"].Values.First().Default.Value * 1.2).ToString()"
```

If you change `Aggregations: ["Maximum"]` to `Aggregations: ["Average"]`, the rule continues to work without modification.

### Metric Validation

Azure Autoscaler includes built-in protection against broken or unreliable metrics from Azure Monitor. Sometimes Azure metrics can return incorrect values (zeros, nulls, or anomalous data), which could lead to dangerous scaling decisions.

**How It Works:**

When you configure `ValidValueMin` and/or `ValidValueMax` on a metric, the autoscaler validates **every data point** in the metric's time series. Both bounds are **inclusive** (values equal to the min/max are considered valid). If even a single data point falls outside the valid range:

1. The individual data point is marked as invalid with a detailed reason
2. The entire metric is marked as invalid
3. All scaling rules that depend on this metric are automatically skipped
4. Detailed warnings are logged showing which values failed validation

**Example:**

```yaml
Metrics:
  storage_used:
    Name: storage_used
    Window: 00:05
    ValidValueMin: 1048576  # 1MB - reject values < 1MB (1MB itself is valid)
    ValidValueMax: 1099511627776  # 1TB - reject values > 1TB (1TB itself is valid)
```

**Important:** Both bounds are **inclusive**:
- `ValidValueMin: 100` means values >= 100 are valid (100 is valid, 99 is invalid)
- `ValidValueMax: 1000` means values <= 1000 are valid (1000 is valid, 1001 is invalid)

**When to Use:**

- **Storage metrics** (`storage_used`, `allocated_data_storage`): Set `ValidValueMin` to prevent accepting zero values when you know the database contains data (e.g., `ValidValueMin: 1048576` rejects values < 1MB)
- **Percentage metrics** (`dtu_consumption_percent`, `cpu_percent`): Set `ValidValueMax: 100` to catch invalid values (100% is valid, 101% is invalid)
- **IOPS/throughput metrics**: Set reasonable bounds based on your SKU limits

**Benefits:**

- **Prevents dangerous scaling**: Won't shrink storage below actual usage when metrics return zeros
- **Complete visibility**: Logs show exactly which data points failed and why
- **Automatic protection**: Rules are skipped automatically when metrics are invalid
- **Zero code changes**: All configuration-based via YAML

**Debug Logging:**

When running with debug logging enabled, you'll see detailed metric validation output:

```
Metric=storage_used, valid=False, values=5, aggregation=avg, valuedetail=524288000, 520000000, !0, !0, 518000000, reason="2 out of 5 data points are invalid (e.g., Value 0.00 is below minimum valid value 1048576.00 at 2026-02-08 10:12:00)"
```

Invalid values are prefixed with `!` to make them easy to spot. This makes it easy to diagnose when and why Azure metrics are returning bad data.

### Forecast Metrics and ForecastMode

Forecasting is configured at the metric level and works in two phases:

1. **Baseline forecast**: with `ForecastEnable: true`, the autoscaler uses historical data (`ForecastTimeRange`) and slot granularity (`ForecastSlotMinutes`) to produce a projected value per day-of-week and time slot.
2. **Optional snap transform**: `ForecastMode` can transform the baseline projection before it is used by scaling rules.

#### Baseline forecast details (affinity + capped-point handling)

Baseline projection uses compatible historical days and these affinity parameters:

- `ForecastAffinitySameDayFactor` (default `1.0`): weight when sample day equals target day.
- `ForecastAffinityWeekdayFactor` (default `0.3`): weight for weekday-to-weekday (different day).
- `ForecastAffinityWeekendFactor` (default `0.3`): weight for weekend-to-weekend (different day).

The service also supports capped-point compensation using `ForecastMetricMax`:

- `ForecastCappedCorrectionThreshold` (default `0.95`): if observed usage is above this fraction of available capacity, point is considered likely capped.
- `ForecastCappedCorrectionFactor` (default `1.2`): multiplier applied to capped points before baseline aggregation (for example, `1.20` increases capped points by 20%).

This helps differentiate cases like:

- `50 / 50` capacity (likely capped, should be treated as potentially higher), versus
- `50 / 100` capacity (not capped, keep value unchanged).

If `ForecastMode` is unset or `Raw`, the baseline is used as-is. The supported values are:

- `Raw`: baseline only (no transform)
- `Anchors`: fixed interval boundaries (`ForecastSnapAnchorHours`)
- `AnchorWindow`: configured windows (`ForecastAnchorWindows`) with optimized change moment
- `Snap`: rolling window percentile using `ForecastSnapStepWindows`

Use the generated forecast metric (`<metricId>_forecast`) in scaling rules when you want decisions driven by projected demand instead of only recent observations.

For the optional parameters that control how the baseline grid itself is built (aggregation mode, recency decay, temporal smoothing, and boost factor) see **[Forecast baseline parameters](forecast-baseline-parameters.md)**.

#### Detailed ForecastMode behavior (Anchors, AnchorWindow, Snap)

`Raw` needs no extra settings; when omitted or set to `Raw`, the baseline forecast is used directly.

##### Anchors (fixed schedule boundaries)

Use `Anchors` when you want stable blocks in the day and predictable transition points.

- Configure `ForecastSnapAnchorHours` with one or more `HH:mm` values.
- Those values define interval boundaries for the full day.
- For each interval, autoscaler computes one percentile value from baseline samples in that interval.
- Every slot inside the interval uses that same snapped value.

**Example (2 daily intervals: 05:00-20:00 and 20:00-05:00):**

```yaml
Metrics:
  workload_absolute:
    Name: custom_workload_absolute
    Window: 00:05
    Aggregations: ["Average"]
    ForecastEnable: true
    ForecastMetricMax: "total_capacity"
    ForecastMode: Anchors
    ForecastSnapAnchorHours: ["05:00", "20:00"]
    ForecastSnapPercentile: 95
```

##### AnchorWindow (windows with optimized change moment)

Use `AnchorWindow` when you know important time windows (for example business hours), but you want the switch moment inside each segment to adapt based on observed patterns.

- Configure `ForecastAnchorWindows` as `HH:mm-HH:mm` ranges (you can define multiple windows).
- Windows can wrap around midnight (for example `20:00-03:00`).
- Autoscaler computes per-segment percentiles and selects change timing to reduce overprovisioning while respecting percentile constraints.
- This mode is useful when strict fixed boundaries are too rigid but you still want time-aware behavior.

**Example (night and early-morning windows):**

```yaml
Metrics:
  workload_absolute:
    Name: custom_workload_absolute
    Window: 00:05
    Aggregations: ["Average"]
    ForecastEnable: true
    ForecastMetricMax: "total_capacity"
    ForecastMode: AnchorWindow
    ForecastAnchorWindows: ["20:00-23:00", "03:00-07:00"]
    ForecastSnapPercentile: 90
```

##### Snap (rolling look-ahead window)

Use `Snap` when you want smoothing based on the current slot plus upcoming slots, without defining explicit clock windows.

- Configure `ForecastSnapStepWindows` as number of consecutive slots to evaluate (`current + next N-1`).
- For each slot, autoscaler computes percentile over that rolling window.
- If `ForecastSnapStepWindows` is missing, default is `3`.
- If `ForecastSnapStepWindows` is `0` or negative, it is clamped to `1`.

**Example (3-slot look-ahead at 95th percentile):**

```yaml
Metrics:
  workload_absolute:
    Name: custom_workload_absolute
    Window: 00:05
    Aggregations: ["Average"]
    ForecastEnable: true
    ForecastMetricMax: "total_capacity"
    ForecastMode: Snap
    ForecastSnapStepWindows: 3
    ForecastSnapPercentile: 95
```

#### Metric selection guidance for stable forecasting

For stable scaling outcomes, prefer forecasting on a metric that represents an **absolute capacity/value** related to the target dimension (for example, available DTU) instead of only relative utilization percentages. Absolute metrics make it easier to map forecast outputs to concrete scale targets and reduce oscillation risk when utilization percent changes due to denominator shifts.

Some resource types do not expose a native absolute metric. In those cases, generate an equivalent signal as a **custom metric** (for example, current capacity, available headroom, or queue-backed demand translated to required units). This tool supports sending and using custom metrics, so forecast inputs can be standardized even when Azure Monitor does not provide the exact built-in metric you need.

### Scaling Rules

A scaling rule determines a target value for one of the resources dimensions. A dimension is an attribute on the target resources (i.e. Dtu, PerDatabaseMaxCapacity, and MaxDataBytes for elastic pools, IOPS or MaxSizeBytes for FileShares), consider that:

* A resource can have more than one Dimension and these dimensions might have dependencies (i.e. the provisioned storage in an Azure Sql Elastic Pool is dependant on the provisioned DTU's). You do not have to worry about this. Create a scaling rule that actuates on the dimension that you are interested in and the system will automatically determine the smallest compatible value for the other dimensions if needed.
* The Autoscaler dimensions **do not always match** one to one the dimensions of the real Azure Resource. I.e. the MySqlFlexible server exposes SKU and CoreCount dimensions, but the Azure resource only know about SKU. The autoscaler will automatically translate these virtual dimensions into what the target resource is expecting (i.e. if you specify a CoreCount,  it will find the nearest SKU that complies with your request). The purpose of this is to facilitate making decisions on resource metrics that will not reflect directly SKU definitions.

A scaling rule consists of three basic concepts:

* **The scaling strategy**: this determines how the scaling rule will be evaluated.
* **The dimension**: what dimension will this rule manipulate
* Others: depending on the strategy used, different attributes will govern the behavior of the rule

### Scaling Rule Strategy Fixed

```yaml
#######################################
# Example to always have 50GB or extra
# 20% disk space provisioned in file shares
# whatever is greater
#######################################
        ScalingRules:
          fixed:
            ScalingStrategy: Fixed
            Dimension: MaxDataBytes
            # Fix target of extra 50GB or 20% additional of current storage, whatever is greater.
            ScaleTarget: "(data) => (Math.Max(data.Metrics[\"storage_used\"].Values.First().Average.Value + (50.1*1024*1024*1024), data.Metrics[\"storage_used\"].Values.First().Average.Value * 1.2)).ToString()"
```

You can also use `DimensionValueMax` and `DimensionValueMin` to bound the calculated value:

```yaml
        ScalingRules:
          fixed:
            ScalingStrategy: Fixed
            Dimension: Dtu
            # Calculate target based on metrics, but ensure it stays within bounds
            ScaleTarget: "(data) => (data.Metrics[\"dtu_consumption_percent\"].Values.First().Average.Value * 2).ToString()"
            DimensionValueMax: "200"  # Never scale above 200 DTU
            DimensionValueMin: "50"   # Never scale below 50 DTU
```

> [!NOTE]
> The `DimensionValueCeilingStep` feature is only available for the **Fixed** strategy, not for Autoadjust.

You can use `DimensionValueCeilingStep` to round the calculated value up to the nearest multiple of a step size:

```yaml
        ScalingRules:
          fixed:
            ScalingStrategy: Fixed
            Dimension: Dtu
            # Calculate target based on metrics, rounded up to nearest 10
            ScaleTarget: "(data) => (data.Metrics[\"dtu_consumption_percent\"].Values.First().Average.Value * 2).ToString()"
            DimensionValueCeilingStep: "10"  # Round up to nearest multiple of 10 (e.g., 47 -> 50, 51 -> 60)
            DimensionValueMax: "200"  # Never scale above 200 DTU
            DimensionValueMin: "50"   # Never scale below 50 DTU
```

The ceiling step is applied before min/max bounds are checked. For example, if the calculated value is 47 and the step is 10, it will be rounded to 50. If 50 is below the minimum, the minimum value will be used instead.

The fixed strategy is useful to apply minimum values to resource dimensions. I.e. you might have an Azure SQL server scale DTU based on load, but you do not want it to go below a certain threshold, used fixed to set it. Remember that the scaler uses the greatest of all proposed values by scaling rules.

Supported attributes are:

* **Dimension**: the dimensions this rule actuates on
* **ScaleTarget**: the dimension target value. You can use .Net lambda expressions here to analyze and make decisions based on the metrics.
* **DimensionValueMax**: (Optional) Upper limit that the calculated ScaleTarget value will be capped to. If the ScaleTarget calculation results in a value greater than DimensionValueMax, the maximum will be used instead.
* **DimensionValueMin**: (Optional) Lower limit that the calculated ScaleTarget value will be capped to. If the ScaleTarget calculation results in a value less than DimensionValueMin, the minimum will be used instead.
* **DimensionValueCeilingStep**: (Optional) Step size for ceiling rounding. When set, the calculated ScaleTarget value will be rounded up to the nearest multiple of this step size. The ceiling operation is applied before min/max bounds are checked. For example, with a step of 10: 47 rounds to 50, 50 stays 50, 51 rounds to 60.

### Scaling Rule Strategy Autoadjust

```yaml
          autoadjust:
            ScalingStrategy: Autoadjust
            Dimension: Dtu
            ScaleUpCondition: "(data) => data.Metrics[\"dtu_consumption_percent\"].Values.Take(3).Average() > 85" # Average DTU > 85% for 3 minutes
            ScaleDownCondition: "(data) => data.Metrics[\"dtu_consumption_percent\"].Values.Take(5).Average() < 60" # Average DTU < 60% for 5 minutes
            ScaleUpTarget: "(data) => data.NextDimensionValue(1)" # You could actually specify DTU number manually, and system will find closest valid tier
            ScaleDownTarget: "(data) => data.PreviousDimensionValue(1)" # You could actually specify DTU number manually, and system will find closest valid tier
            ScaleUpCooldownSeconds: 180
            ScaleDownCoolDownSeconds: 3600
            DimensionValueMax: "200"
            DimensionValueMin: "50"
```

The autoadjust is designed to react based on metrics:

* **Dimension**: What resource dimension will this rule be manipulating. ScaleUpTarget and ScaleDownTarget must return compatible values with this dimension. I.e. if the dimensions is SKU, they must return valid SKU's for the resource.
* **ScaleUpCondition**: Boolean indicating that the value returned by ScaleUpTarget should be used.
* **ScaleDownCondition**: Boolean indicating that the value returned by ScaleDownTarget should be used.
* **ScaleUpCooldownSeconds**: After a scale operation, minimum amount of time required to allow a new upscale operation.
* **ScaleDownCooldDownSeconds**: After a scale operation, minimum amount of time required to allow a new downscale operation.
* **DimensionValueMax**: Upper limit that both ScaleUpTarget and ScaleDownTarget will be capped. (yes, you could take care of this within the lambda expression itself, it is just here for convenience)
* **DimensionValueMin**: Lower limit that both ScaleUpTarget and ScaleDownTarget will be capped. (yes, you could take care of this within the lambda expression itself, it is just here for convenience)
* **LastScaleMetric** (optional): Name of a metric in the `Metrics` section that reflects when this rule’s target dimension effectively changed (for example, an AKS `node_count` metric backed by a custom metric). The autoscaler walks that metric’s time series and finds the last point where the value changed; that timestamp becomes a candidate for the rule’s last scale time.
* **LastScale** (internal): A per‑rule timestamp that remembers the last effective scale time used for cooldowns. On each evaluation, the autoscaler takes the newest of: the resource‑level `LastScale`, the rule’s own `LastScale`, and the metric‑based last change derived from `LastScaleMetric`, then writes the winner back into `LastScale`.

> [!NOTE]
>
> If neither scale up or scale down conditions are met, the target value for the dimensions will be the current dimension of the resource.

