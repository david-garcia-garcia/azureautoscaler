## Installation

The Azure Autoscaler application is distributed as a container image, and needs to be deployed to a runtime of your choice. You also need to ensure the application is granted permissions to act on your Azure Resources in order to perform scaling operations.

### Locally with docker

> [!Note]
>
> You can run the autoscaler locally using docker and authorize it against your Azure resources using your Entra account. To do so, use DeviceCodeCredential option in AzureCredentialType in the [configuration file](../configuration.md#global-structure).

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

