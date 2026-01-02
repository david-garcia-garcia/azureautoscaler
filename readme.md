# Azure Autoscaler

Azure Autoscaler is a powerful, self-hosted solution for automatically scaling Azure resources based on real-time metrics, schedules, and usage forecasts. It helps optimize costs while maintaining performance for various Azure services.

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
| AKS Node Pools | MinNodeCount, MaxNodeCount |  |  |
| Azure SQL Elastic Pools | Dtu, MaxDataBytes |  |  |
| Azure SQL Databases | Dtu, MaxDataBytes |  | MaxDataBytes supports DTU and VCore models (see notes below) |
| Azure MySQL Flexible Server | Sku, Iops, CoreCount | custom_sku_corecount_forecast |  |
| Azure Files | ProvisionedStorage |  |  |

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
    image: davidbcn86/azureautoscaler:v2.0.0-private.beta.10
    volumes:
      - ./config.yml:/app/config.yml
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
      TimestampFormat: "HH:HH:mm:ss"
Resources:
  - Resources:
      stdevappsharedfiles:
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
  location            = "your-locastion"
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
    namespace = local.aks_namespace_name
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
  # Ojo que el formato de esto es imporantisimo y sigue un patrón concreto
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

# The applicaton configuration
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
        app = local.deployment_app_label
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
      TimestampFormat: "HH:HH:mm:ss"
Resources:
  - R0
  - R1
  - R2
```

## Resource structure

### Scaling configurations

In this simple example, we will be scaling two Azure Sql Elastic Pools so that they will have 50 DTU during working hours, 20 DTU during the night, and 10 DTU during weekends.

```yaml
  - Resources:
      sbssqldevshared_dev_sbssqlpooldevshared:
        ResourceId: "/subscriptions/mysubscription/resourceGroups/mysourcegroup/providers/Microsoft.Sql/servers/sql0/elasticPools/pool1"
      sbssqldevshared_dev_sbssqlpooldevshared2:
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
      sbssqldevshared:
        ResourceId: "/subscriptions/mysubscription/resourceGroups/myresourcegroup/providers/Microsoft.Sql/servers/sql0/elasticPools/{.*}"
```

If you want to target a specific subset of resource, you can use a regular expression:

```yaml
  - Resources:
      sbssqldevshared:
        ResourceId: "/subscriptions/mysubscription/resourceGroups/myresourcegroup/providers/Microsoft.Sql/servers/sql0/elasticPools/{^pool-}"
```

Resource expansion works for:

* File shares in a storage account
* Node pools in an AKS cluster
* SQL Databases in an Azure SQL Server
* SQL Elastic Pools in an Azure SQL Server

### Metrics

Because you need to make real time decisions based on resource metrics, each Scaling Configuration can declare a set of metrics that will be evaluated on the resource and made available for usage in the scaling rules.

```yaml
  - Resources:
      sbssqldevshared_dev_sbssqlpooldevshared:
        ResourceId: "/subscriptions/xx/resourceGroups/rg/providers/Microsoft.Storage/storageAccounts/account/fileServices/default/shares/share"
    ScalingConfigurations:
      Baseline:
        Metrics:
          FileCapacity:
            # Name is the name of the metric in Azure Metrics
            Name: FileCapacity
            # (OPTIONAL) resourceID indicates what resource to get the metric from. Sometimes the metrics for some resource actually belong to the parent resource, and are accesed through the usag eof splits. If not specified, the actual ID of the configured resource will be used.
            ResourceId: "/subscriptions/${subscriptionId}/resourceGroups/${resourceGroupName}/providers/Microsoft.Storage/storageAccounts/${storageAccountName}/fileServices/default"
            # Evaluation window. Metric evaluation will retrieve data from (Now - Window) to Now
            Window: 02:00:00
            # TimeGrain, as defined in the Azure metricsA
            TimeGrain: 01:00:00 
            # (OPTIONAL) SplitName. You can use resource replace groups in the splitname.
            SplitName: "FileShare"
            # (OPTIONAL) SplitValue. You can use resource replace groups in the splitname.
            SplitValue: "apptemp"
            # (OPTIONAL) Aggregations. What aggregations to be retrieved for the metric. If not specified defaults
            # to "Average". Careful with this because the default Average is not the default behaviour for all metrics
            # in the portal, where some resource have different aggregations set as default.
            Aggregations: ["Total"]
            # (OPTIONAL) Manipulate the individual metric values before sendig them to evaluation 
            Transform: "(value) => value / (1000 * 1000 * 1000)" # Convert to Gb
            # (OPTIONAL) AllowFail. When set to true, allows the metric to fail to load without throwing an exception.
            # Instead, a debug message will be logged and the metric will be skipped. Defaults to false.
            # Useful when a metric may not be available for certain resource configurations.
            # For example, dtu_consumption_percent is not available for VCore model SQL databases, only for DTU model.
            AllowFail: false
          storage_used:
            Name: storage_used
            Window: 00:05
          dtu_consumption_percent:
            Name: dtu_consumption_percent
            Window: 00:05
            # This metric is only available for DTU model databases, not VCore model
            AllowFail: true
```

### Scaling Rules

A scaling rule determines a target value for one of the resources dimensions. A dimensions is an attribute on the target resources (i..e DTU for elastic pools, IOPS or MaxSyzeBytes for FileShares), consider that:

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
            ScaleUpTarget: "(data) => data.NextDimensionValue(1)" # You could actually specificy DTU number manually, and system will find closes valid tier
            ScaleDownTarget: "(data) => data.PreviousDimensionValue(1)" # You could actually specificy DTU number manually, and system will find closes valid tier
            ScaleUpCooldownSeconds: 180
            ScaleDownCoolDownSeconds: 3600
            DimensionValueMax: "200"
            DimensionValueMin: "50"
```

The autoadjust is designed to react based on metrics:

* **Dimension**: What resource dimension will this rule be manipulating. ScaleUpTarget and ScaleDownTarget must return compatible values with this dimension. I.e. if the dimensions is SKU, they must return valid SKU's for the resource.
* **ScaleUpCondition**: Boolean indicating that the value returned by ScaleUpTarget should be used.
* **ScaleDownCondition**: Boolean indicating that the value return by ScaleDownTarget should be used.
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
            ScaleUpCondition: "(data) => data.Metrics[\"node_cpu_usage_percentage\"].Values.Select(i => i.Average).Take(3).Average() > 80" # Average DTU > 85% for 3 minutes
            ScaleDownCondition: "(data) => data.Metrics[\"node_cpu_usage_percentage\"].Values.Select(i => i.Average).Take(10).Average() < 60" # Average DTU < 60% for 5 minutes
            ScaleUpTarget: "(data) => data.NextDimensionValue(1)"
            ScaleDownTarget: "(data) => data.PreviousDimensionValue(1)"
            ScaleUpCooldownSeconds: 300
            ScaleDownCooldownSeconds: 1200
```

## SQL Elastic Pool

```yaml
  - Resources:
      sbssqldevshared_pools:
        ResourceId: "/subscriptions/mysubscriptionid/resourceGroups/myresourcegroup/providers/Microsoft.Sql/servers/mypool/elasticPools/{.*}"
      sbsmssqlprodshared_pools:
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
            ScaleUpCondition: "(data) => data.Metrics[\"dtu_consumption_percent\"].Values.Select(i => i.Average).Take(3).Average() > 80" # Average DTU > 85% for 3 minutes
            ScaleDownCondition: "(data) => data.Metrics[\"dtu_consumption_percent\"].Values.Select(i => i.Average).Take(5).Average() < 60" # Average DTU < 60% for 5 minutes
            ScaleUpTarget: "(data) => data.NextDimensionValue(1)" # You could actually specificy DTU number manually, and system will find closes valid tier
            ScaleDownTarget: "(data) => data.PreviousDimensionValue(1)" # You could actually specificy DTU number manually, and system will find closes valid tier
            ScaleUpCooldownSeconds: 180
            ScaleDownCoolDownSeconds: 3600
            DimensionValueMax: "200"
            DimensionValueMin: "50"
      MaxDataBytes:
        Metrics:
          storage_used:
            Name: storage_used
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
            ScaleTarget: "(data) => (Math.Max(data.Metrics[\"storage_used\"].Values.First().Average.Value + (50.1*1024*1024*1024), data.Metrics[\"storage_used\"].Values.First().Average.Value * 1.2)).ToString()"
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
            ScaleUpTarget: "(data) => data.NextDimensionValue(1)" # You could actually specificy DTU number manually, and system will find closes valid tier
            ScaleDownTarget: "(data) => data.PreviousDimensionValue(1)" # You could actually specificy DTU number manually, and system will find closes valid tier
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

## MySQL Flexible Server

```yaml
  - Resources:
      mysql-dev-mysql0:
        ResourceId: "/subscriptions/mysubscriptionid/resourceGroups/myresourcegroup/providers/Microsoft.DBforMySQL/flexibleServers/myserver"
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
            ScaleUpCondition: "(data) => data.Metrics[\"cpu_percent\"].Values.Select(i => i.Average).Take(3).Average() > 85" # Average DTU > 85% for 3 minutes
            ScaleDownCondition: "(data) => data.Metrics[\"cpu_percent\"].Values.Select(i => i.Average).Take(5).Average() < 60" # Average DTU < 60% for 5 minutes
            ScaleUpTarget: "(data) => data.NextDimensionValue(1)" # You could actually specificy DTU number manually, and system will find closes valid tier
            ScaleDownTarget: "(data) => data.PreviousDimensionValue(1)" # You could actually specificy DTU number manually, and system will find closes valid tier
            ScaleUpCooldownSeconds: 180
            ScaleDownCoolDownSeconds: 3600
            DimensionValueMax: "Standard_B4ms"
            DimensionValueMin: "Standard_B1ms"
      ForecastDaily:
        Metrics:
          custom_sku_corecount_forecast:
            Name: custom_sku_corecount_forecast # This metrics provides a SKU forecast based on last 90 days of activity so that no resizing is needed within the proposed time window. It's internals are currently hardcoded and should be parameterized. Useful because MySQL resizing is very disruptive (i.e. ~5 min downtime). 
        TimeWindow:
          Days: All
          Months: All
          StartTime: "00:00"
          EndTime: "23:59"
          TimeZone: "Romance Standard Time"
        ScalingRules:
          fixed:
            ScalingStrategy: Fixed
            Dimension: Sku
            ScaleTarget: "(data) => (np(data.Metrics[\"custom_sku_corecount_forecast\"].Values.FirstOrDefault()).CustomString).ToString()"
      MinSku:
        TimeWindow:
          Days: All
          Months: All
          StartTime: "00:00"
          EndTime: "23:59"
          TimeZone: "Romance Standard Time"
        ScalingRules:
          fixed:
            ScalingStrategy: Fixed
            Dimension: Sku
            ScaleTarget: "(data) => (\"Standard_B1ms\")"
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
      stdevappsharedfiles:
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

