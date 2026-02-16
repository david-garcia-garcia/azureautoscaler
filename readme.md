# Azure Autoscaler

Azure Autoscaler is a powerful, self-hosted solution for automatically scaling Azure resources based on real-time metrics, schedules, and usage forecasts. It helps optimize costs while maintaining performance for various Azure services.

## Why Azure Autoscaler?

Azure provides built-in autoscaling capabilities for some resources, but there are significant gaps that Azure Autoscaler addresses:

* **AKS Node Pool Limitations**: The built-in AKS node autoscaler is slow to respond to load changes and can easily fail under load peaks. Azure Autoscaler provides faster, more reliable scaling based on actual CPU usage metrics rather than just pod scheduling requests.

* **Missing PaaS Autoscaling**: Many Azure PaaS resources lack native autoscaling capabilities:
  * Azure SQL Elastic Pools
  * Azure SQL Databases
  * Azure MySQL Flexible Server
  * Azure PostgreSQL Flexible Server
  * Azure Files

Azure Autoscaler fills these gaps by providing intelligent, metric-based autoscaling for all these resources, helping you optimize costs while maintaining performance during peak loads.

## Features

- **Real-time Metric Based Scaling**: Scale resources based on actual usage patterns and performance metrics
- **Schedule-Based Scaling**: Automatically adjust resources based on predefined schedules
- **Usage Forecasting**: Predict future resource needs using AI-powered forecasting
- **Self-hosted Solution**: Deploy as a containerized application in AKS or Azure Container Services
- **Flexible Metric Analysis**: Create custom scaling rules using natural Lambda Expressions
- **Multidimensional Scaling**: Target different dimensions for the same resource

## Supported Resource Types

| Resource Type | Supported Dimensions | Custom Metrics | Notes |
|--------------|---------------------|----------------|-------|
| AKS Node Pools | MinNodeCount | Custom metrics via `CustomMetrics` (e.g. total cores / memory) | MaxNodeCount is preserved as a constraint but not scaled |
| Azure SQL Elastic Pools | Dtu, MaxDataBytes | |  |
| Azure SQL Databases | Dtu, MaxDataBytes | | MaxDataBytes supports DTU and VCore models (see notes below) |
| Azure MySQL Flexible Server | Sku, Iops, CoreCount | Custom metrics via `CustomMetrics` |  |
| Azure PostgreSQL Flexible Server | Sku, Iops, CoreCount | Custom metrics via `CustomMetrics` |  |
| Azure Files | ProvisionedStorage, Throughput |  | Although Throughput is not a real dimension in Azure for a file share, it is exposed as an actionable dimension and the file share provisioned storage is scaled to meet the desired throughput targets |
| Azure DevOps Parallel Jobs | HostedParallelJobs, PrivateParallelJobs | custom_azdo_queued_hosted, custom_azdo_queued_self_hosted, custom_azdo_running_hosted, custom_azdo_running_self_hosted, custom_azdo_available_hosted, custom_azdo_available_self_hosted | Uses undocumented Commerce API. Requires PAT with billing permissions. |

## Licensing

Azure Autoscaler is released under the Business Source License (BSL) 1.1. The source code is available for viewing, modification, and non-production use. For production use, please see the licensing terms below or contact the licensor for commercial licensing options.

### Prebuilt Docker Images

Prebuilt Docker images are available for convenience and require a license key to unlock full functionality. License keys can be purchased at [www.azureautoscaler.com](https://www.azureautoscaler.com).

**Prebuilt Image Location:**
- Docker Hub: [`davidbcn86/azureautoscaler`](https://hub.docker.com/repository/docker/davidbcn86/azureautoscaler/general)

**Limited Functionality (Unlicensed):**
When using a prebuilt image without a valid license key, the following limitations apply:
- **Maximum 2 resources**: Only two Azure resources can be scaled
- **Trace logging only**: Limited to trace-level logging

### Building Your Own Images

You can build your own Docker images from the source code. However, Azure Autoscaler uses the **Business Source License (BSL) 1.1**, so usage is limited to what the license specifies. The BSL permits copying, modification, derivative works, redistribution, and **non-production use**. For production use, you must comply with the license terms—either through the Additional Use Grant (if any) or by purchasing a commercial license. See the [LICENSE](LICENSE) file for the full terms. The [Installation](#installation) section has build instructions.

### Why Licensing?

The licensing model helps support the ongoing development and maintenance of Azure Autoscaler. By purchasing a license, you're directly contributing to:
- Continued feature development
- Bug fixes and security updates
- Documentation improvements
- Community support

This approach allows us to keep the source code open and freely available while ensuring sustainable development of the project.

## Installation

The Azure Autoscaler application is distributed as a container image, and needs to be deployed to a runtime of your choice. You also need to ensure the application is granted permissions to act on your Azure Resources in order to perform scaling operations.

### Locally with docker

> [!Note]
>
> You can run the autoscaler locally using docker and authorize it against your Azure resources using your Entra account. To do so, use DeviceCodeCredential option in AzureCredentialType in the configuration file.

Create a compose.yaml file:

```yaml
services:
  app:
    image: davidbcn86/azureautoscaler:latest
    volumes:
      - ./config.yml:/app/config.yml
    environment:
      # Optional: Set your license key to unlock full functionality
      # Purchase a license at https://www.azureautoscaler.com
      # AUTOSCALER_LICENSE: "your-license-jwt-token-here"
```

Create a configuration file:

> [!Tip]
> 
> A template configuration file is available at `autoscaler/config.yml.example`. Copy it to `config.yml` and replace all placeholder values (subscription IDs, resource group names, resource names) with your actual Azure resource identifiers.

```yaml
DefaultResourceWhatIf: true
DefaultResourceEnabled: true
DefaultResourceFrequency: 10m
AzureCredentialType: DeviceCodeCredential # Only for local testing
Logging:
  LogLevel:
    Default: "Debug"
    Microsoft.Hosting.Lifetime: "Debug"
  Console:
    FormatterName: "simple"
    FormatterOptions:
      SingleLine: true
      TimestampFormat: "HH:mm:ss"
Resources:
  - Resources:
      myappfiles:
        ResourceId: "/subscriptions/mysubscription/resourceGroups/myresourcegroup/providers/Microsoft.Storage/storageAccounts/mystorageaccount/fileServices/default/shares/{.*}"
    Frequency: 30m
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
            DimensionValueCeilingStep: "1"  # Round up to nearest GB
            DimensionValueMin: "100"  # Minimum 100 GB for Azure Files
```

Start the image with docker:

```powershell
docker compose up
```

### For kubernetes (with terraform examples)

Relevant readings:

* [Use a Microsoft Entra Workload ID on AKS - Azure Kubernetes Service | Microsoft Learn](https://learn.microsoft.com/en-us/azure/aks/workload-identity-overview?tabs=dotnet)
* [Deploy and configure an AKS cluster with workload identity - Azure Kubernetes Service | Microsoft Learn](https://learn.microsoft.com/en-us/azure/aks/workload-identity-deploy-cluster)

To deploy this on Kubernetes we will need:

* A managed identity: to grant permissions for the application
* A federated credential in our cluster
* A configmap with the application configuration
* A deployment with the application itself

```yaml
locals {
   resource_group_name = "your-resource-group"
   aks_namespace = "azureautoscaler"
   aks_info = {
      name = "my-aks-cluster-name"
      resource_group_name = "my-aks-cluster-resource-group"
   }
   app_image = "url-to-image"
   app_image_login_server = ""
   app_image_username = ""
   app_image_password = ""
}

resource "kubernetes_secret" "acr_secret" {
  provider = kubernetes.cluster
  metadata {
    name      = "azureautoscaler"
    namespace = local.aks_namespace
  }

  data = {
    ".dockerconfigjson" = jsonencode({
      auths = {
        "${local.app_image_login_server}" = {
          username = azurerm_container_registry_token.token.name
          password = azurerm_container_registry_token_password.password.password1[0].value
          auth     = base64encode("${local.app_image_username}:${local.app_image_password}")
        }
      }
    })
  }

  type = "kubernetes.io/dockerconfigjson"
}

# Cluster info
data "azurerm_kubernetes_cluster" "cluster" {
  name                = local.aks_info.name
  resource_group_name = local.aks_info.resource_group_name
}

# Identity for the application
resource "azurerm_user_assigned_identity" "app" {
  name                = "azureautoscaler"
  resource_group_name = local.resource_group_name
  location            = "your-location"
}

# Assign permissions to resources that the application will be manipulating
resource "azurerm_role_assignment" "identity_azureresource_contributor" {
  for_each                         = {
    r0 = "/subscriptions/xxx/resourceGroups/rg-a/providers/Microsoft.ContainerService/managedClusters/akscluster/agentPools/linuxpool"
    r1 = "/subscriptions/xxx/resourceGroups/rg-b/providers/Microsoft.DBforMySQL/flexibleServers/mysql" 
  }
  scope                            = each.value
  role_definition_name             = "Contributor"
  principal_id                     = azurerm_user_assigned_identity.app.principal_id
  skip_service_principal_aad_check = false
}

# Service account
resource "kubernetes_service_account" "app" {
  metadata {
    name      = "workloadidentity-azureautoscaler"
    namespace = local.aks_namespace
    annotations = {
      "azure.workload.identity/client-id" = var.application_identity.client_id
    }
  }
}

# Federate the credential in your cluster
resource "azurerm_federated_identity_credential" "federated_credential" {
  name                = "k8s-fed-${data.azurerm_kubernetes_cluster.cluster.name}-${kubernetes_service_account.app.metadata[0].name}"
  resource_group_name = local.resource_group_name
  parent_id           = var.application_identity.id
  # Note: The format of this is important and follows a specific pattern
  subject  = "system:serviceaccount:${local.aks_namespace}:${kubernetes_service_account.app.metadata[0].name}"
  issuer   = data.azurerm_kubernetes_cluster.cluster.oidc_issuer_url
  audience = ["api://AzureADTokenExchange"]

  # Optionally, you can specify a description and a list of claims_mapping
  # description = "Federated Identity Credential for AKS Workload"
  # claims_mapping = [...]
}

# Namespace
resource "kubernetes_namespace" "app" {
  metadata {
    name = local.aks_namespace
  }
}

# The application configuration
resource "kubernetes_config_map" "app_config_yml" {
  metadata {
    name      = "azureautoscaler-config-yml"
    namespace = kubernetes_namespace.app.metadata[0].name
  }
  data = {
    "config.yml" = file("settings.yml") # Here read your application configuration file
  }
}

# The deployment
resource "kubernetes_deployment" "app" {
  wait_for_rollout = false
  metadata {
    name      = "azureautoscaler"
    namespace = kubernetes_namespace.app.metadata[0].name
    labels = {
      app = "azureautoscaler"
    }
  }
  spec {
    selector {
      match_labels = {
        app = "azureautoscaler"
      }
    }
    template {
      metadata {
        labels = {
          app                           = "azureautoscaler"
          "azure.workload.identity/use" = "true" # Important
        }
      }
      spec {
        image_pull_secrets {
          name = kubernetes_secret.acr_secret.metadata[0].name
        }

		# Important
        service_account_name = kubernetes_service_account.app.metadata[0].name

        container {
          name  = "app"
          image = local.app_image
          env {
            # Optional: Set your license key to unlock full functionality
            # Purchase a license at https://www.azureautoscaler.com
            # name  = "AUTOSCALER_LICENSE"
            # value = "your-license-jwt-token-here"
          }
          volume_mount {
            name       = "config-yml"
            mount_path = "/app/config.yml"
            sub_path   = "config.yml"
          }
        }
        volume {
          name = "config-yml"
          config_map {
            name = kubernetes_config_map.app_config_yml.metadata[0].name
          }
        }
      }
    }
  }
}


```

### For container services

Relevant readings:

* [Enable managed identity in container group - Azure Container Instances | Microsoft Learn](https://learn.microsoft.com/en-us/azure/container-instances/container-instances-managed-identity)
* [Config maps for Azure Container Instances (Preview) - Azure Container Instances | Microsoft Learn](https://learn.microsoft.com/en-us/azure/container-instances/container-instances-config-map?tabs=cli)

## The configuration file

### Configuration lifecycle

The application will parse, validate and load the configuration once during container startup. Any modifications to the configuration require a container restart.

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

You can query metrics of resources different to the one you are scaling. I.e. if you are scaling a Windows node pool in AKS, you will need to retrieve metrics from the underlying VMSS. In those cases, the VMSS is available as a replacement token (see examples further in this document).

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

### Scaling Rules

A scaling rule determines a target value for one of the resources dimensions. A dimension is an attribute on the target resources (i.e. DTU for elastic pools, IOPS or MaxSizeBytes for FileShares), consider that:

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

> [!NOTE]
>
> If neither scale up or scale down conditions are met, the target value for the dimensions will be the current dimension of the resource.

# Examples

## AKS Node Pool

```yaml
  - Resources: 
      aks_dev_nodepools:
        ResourceId: "/subscriptions/mysubscriptionid/resourceGroups/myresourcegroup/providers/Microsoft.ContainerService/managedClusters/mycluster/agentPools/{.*}"
    Frequency: 5m
    ScalingConfigurations:
      baseline:
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

## SQL Elastic Pool

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

## SQL Database

### DTU Scaling Example

```yaml
  - Resources:
      sqlsrvdatosetg_db:
        ResourceId: "/subscriptions/mysubscription/resourceGroups/databases/providers/Microsoft.Sql/servers/myserver/databases/*"
    Frequency: 5m
    WhatIf: false
    ScalingConfigurations:
      Baseline:
        ScaleDownLockWindowMinutes: 50
        ScaleUpAllowWindowMinutes: 50
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
            ScaleUpCondition: "(data) => data.Metrics[\"dtu_consumption_percent\"].Values.Select(i => i.Average).Take(3).Average() > 85" # Average DTU > 85% for 3 minutes
            ScaleDownCondition: "(data) => data.Metrics[\"dtu_consumption_percent\"].Values.Select(i => i.Average).Take(5).Average() < 60" # Average DTU < 60% for 5 minutes
            ScaleUpTarget: "(data) => data.NextDimensionValue(1)" # You could actually specify DTU number manually, and system will find closest valid tier
            ScaleDownTarget: "(data) => data.PreviousDimensionValue(1)" # You could actually specify DTU number manually, and system will find closest valid tier
            ScaleUpCooldownSeconds: 180
            ScaleDownCoolDownSeconds: 3600
            DimensionValueMax: "200"
            DimensionValueMin: "10"
```

### MaxDataBytes Scaling Example

```yaml
  - Resources:
      sqldb_storage_example:
        ResourceId: "/subscriptions/mysubscription/resourceGroups/databases/providers/Microsoft.Sql/servers/myserver/databases/mydb"
    Frequency: 30m
    WhatIf: false
    ScalingConfigurations:
      Baseline:
        Metrics:
          allocated_data_storage:
            Name: allocated_data_storage
            Window: 02:00:00
            TimeGrain: 01:00:00
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
            # Calculate target based on metrics, but ensure it stays within bounds
            ScaleTarget: "(data) => (data.Metrics[\"allocated_data_storage\"].Values.First().Average.Value / (1024 * 1024 * 1024) + 50).ToString()"  # Current usage + 50GB
            DimensionValueCeilingStep: "1"  # Round up to nearest GB
            DimensionValueMax: "1024"  # Never scale above 1024 GB (VCore) or 250 GB (DTU Standard) - depends on SKU
            DimensionValueMin: "1"       # Never scale below 1 GB (VCore) or 0.1 GB (DTU)
```

> [!NOTE]
> The MaxDataBytes dimension supports both DTU and VCore models. Valid storage sizes depend on the database's SKU configuration:
> - **DTU Model**: ElasticPool (0.1-1024 GB), Standard (0.1-250 GB), Premium (0.1-1024 GB) - specific values only
> - **VCore Model**: General Purpose Provisioned/Serverless (1-1024 GB / 1-512 GB), Business Critical Provisioned (1-1024 GB) - natural GB increments
> - **Not Supported**: Hyperscale (any tier) and Business Critical Serverless
> 
> Even when a database belongs to an elastic pool, MaxDataBytes can be set, but pool storage limits will be enforced.

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
- **ResourceId** (optional): ARM resource ID that receives the metric. If omitted, the current resource’s `ResourceId` is used. You can use the same replacement tokens as in normal metric definitions (e.g. `${virtualMachineScaleSetId}`).
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

## PostgreSQL Flexible Server

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

## Azure DevOps Parallel Jobs

Azure DevOps Parallel Jobs autoscaling allows you to dynamically adjust the number of purchased parallel jobs (both MS-hosted and self-hosted) based on pipeline queue metrics. This helps optimize costs by scaling down when pipelines are idle and scaling up when there's demand.

> [!NOTE]
> This feature uses an undocumented Azure DevOps Commerce API. The API was discovered through the [VSTeam PowerShell module](https://www.powershellgallery.com/packages/VSTeam).

### Prerequisites

- A Personal Access Token (PAT) with the following permissions:
  - **Agent Pools (Read)** - to query job queue metrics
  - **Billing** permissions on the organization

### Firewall Requirements

The following domains must be accessible (HTTPS, port 443):

| Domain | Purpose |
|--------|---------|
| `dev.azure.com` | Standard Azure DevOps APIs (authentication, agent pools, job requests) |
| `azdevopscommerce.dev.azure.com` | Commerce API for getting/setting parallel job counts |

### Resource ID Format

Azure DevOps resources use a custom URI scheme (not ARM resource IDs):

```
azuredevops://{organization}
```

Where `{organization}` is your Azure DevOps organization name (from `dev.azure.com/yourorg`). The organization ID (GUID) is resolved automatically via API.

### Supported Dimensions

| Dimension | Description |
|-----------|-------------|
| HostedParallelJobs | Number of MS-hosted parallel jobs |
| PrivateParallelJobs | Number of self-hosted parallel jobs |

### Custom Metrics

| Metric | Description |
|--------|-------------|
| custom_azdo_queued_hosted | Total queued jobs across all MS-hosted agent pools |
| custom_azdo_queued_self_hosted | Total queued jobs across all self-hosted agent pools |
| custom_azdo_running_hosted | Total running jobs across all MS-hosted agent pools |
| custom_azdo_running_self_hosted | Total running jobs across all self-hosted agent pools |
| custom_azdo_available_hosted | Available hosted capacity (parallel jobs limit - running jobs) |
| custom_azdo_available_self_hosted | Available self-hosted capacity (parallel jobs limit - running jobs) |

### Example Configuration

This example scales self-hosted parallel jobs with different limits for weekdays vs weekends:

```yaml
  - Resources:
      azdo_parallel_jobs:
        ResourceId: "azuredevops://myorg"
        Settings:
          # PAT can be direct value or environment variable reference
          Pat: "env://AZDO_PAT"
    Frequency: 1m
    WhatIf: true  # Set to false to actually change parallel jobs (affects billing!)
    Enabled: true
    ScalingConfigurations:
      # Weekday scaling: more capacity during work days
      SelfHostedWeekdays:
        Metrics:
          queued_self_hosted:
            Name: custom_azdo_queued_self_hosted
          available_self_hosted:
            Name: custom_azdo_available_self_hosted
        TimeWindow:
          Days: Monday, Tuesday, Wednesday, Thursday, Friday
          Months: All
          StartTime: "00:00"
          EndTime: "23:59"
          TimeZone: UTC
        ScalingRules:
          scale_private:
            ScalingStrategy: Autoadjust
            Dimension: PrivateParallelJobs
            DimensionValueMin: "1"   # Keep at least 1 (first self-hosted job is free)
            DimensionValueMax: "4"   # Cap at 4 parallel jobs
            ScaleUpCondition: "(data) => data.Metrics[\"queued_self_hosted\"].Values.First().Average > 2"
            ScaleDownCondition: "(data) => data.Metrics[\"available_self_hosted\"].Values.First().Average > 1"
            ScaleUpTarget: "(data) => data.NextDimensionValue(1)"
            ScaleDownTarget: "(data) => data.PreviousDimensionValue(1)"
            ScaleUpCooldownSeconds: 1200    # 20 min - allow changes to propagate
            ScaleDownCooldownSeconds: 3600  # 1 hour cooldown before scale down
      # Weekend scaling: reduced capacity to save costs
      SelfHostedWeekends:
        Metrics:
          queued_self_hosted:
            Name: custom_azdo_queued_self_hosted
          available_self_hosted:
            Name: custom_azdo_available_self_hosted
        TimeWindow:
          Days: Saturday, Sunday
          Months: All
          StartTime: "00:00"
          EndTime: "23:59"
          TimeZone: UTC
        ScalingRules:
          scale_private:
            ScalingStrategy: Autoadjust
            Dimension: PrivateParallelJobs
            DimensionValueMin: "1"   # Keep at least 1 (first self-hosted job is free)
            DimensionValueMax: "2"   # Cap at 2 parallel jobs on weekends
            ScaleUpCondition: "(data) => data.Metrics[\"queued_self_hosted\"].Values.First().Average > 2"
            ScaleDownCondition: "(data) => data.Metrics[\"available_self_hosted\"].Values.First().Average > 1"
            ScaleUpTarget: "(data) => data.NextDimensionValue(1)"
            ScaleDownTarget: "(data) => data.PreviousDimensionValue(1)"
            ScaleUpCooldownSeconds: 1200    # 20 min - allow changes to propagate
            ScaleDownCooldownSeconds: 1800  # 30 min cooldown before scale down
```

> [!IMPORTANT]
> - Azure DevOps parallel jobs billing is monthly and prorated. Changes take effect immediately but billing adjustments may take time to reflect.
> - The first self-hosted parallel job is free, so scaling to 1 incurs no cost.

