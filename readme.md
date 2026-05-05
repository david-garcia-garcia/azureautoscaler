# Azure Autoscaler

Azure Autoscaler is a powerful, self-hosted solution for automatically scaling Azure resources based on real-time metrics, schedules, and usage forecasts. It helps optimize costs while maintaining performance for various Azure services.

## Table of contents

**Overview**

- [Why Azure Autoscaler?](#why-azure-autoscaler)
- [Features](#features)
- [Supported Resource Types](#supported-resource-types)
- [Licensing](#licensing)
  - [Prebuilt Docker Images](#prebuilt-docker-images)
  - [Building Your Own Images](#building-your-own-images)
  - [Why Licensing?](#why-licensing)

**Documentation**

_Detailed guides live under [`docs/`](docs/). Expand a topic below._

**Installation**

- [Installation overview](docs/installation.md)
  - [Locally with docker](docs/installation.md#locally-with-docker)
  - [For kubernetes (with terraform examples)](docs/installation.md#for-kubernetes-with-terraform-examples)
  - [For container services](docs/installation.md#for-container-services)

**Configuration**

- [The configuration file](docs/configuration.md#the-configuration-file)
  - [Configuration lifecycle](docs/configuration.md#configuration-lifecycle)
- [Global structure](docs/configuration.md#global-structure)
  - [Resource-Specific Logging](docs/configuration.md#resource-specific-logging)
- [Resource structure](docs/configuration.md#resource-structure)
  - [Scaling configurations](docs/configuration.md#scaling-configurations)
  - [Resource Expansion](docs/configuration.md#resource-expansion)
  - [Resource Filter](docs/configuration.md#resource-filter)
  - [Resource Tags](docs/configuration.md#resource-tags)
  - [Metrics](docs/configuration.md#metrics)
  - [Using the Default Property in Rules](docs/configuration.md#using-the-default-property-in-rules)
  - [Metric Validation](docs/configuration.md#metric-validation)
  - [Forecast Metrics and ForecastMode](docs/configuration.md#forecast-metrics-and-forecastmode)
    - [Baseline forecast details (affinity + capped-point handling)](docs/configuration.md#baseline-forecast-details-affinity--capped-point-handling)
    - [Detailed ForecastMode behavior (Anchors, AnchorWindow, Snap)](docs/configuration.md#detailed-forecastmode-behavior-anchors-anchorwindow-snap)
    - [Metric selection guidance for stable forecasting](docs/configuration.md#metric-selection-guidance-for-stable-forecasting)
  - [Scaling Rules](docs/configuration.md#scaling-rules)
  - [Scaling Rule Strategy Fixed](docs/configuration.md#scaling-rule-strategy-fixed)
  - [Scaling Rule Strategy Autoadjust](docs/configuration.md#scaling-rule-strategy-autoadjust)

**Custom metrics**

- [Custom Metrics](docs/custom-metrics.md#custom-metrics)
  - [CustomMetrics configuration options](docs/custom-metrics.md#custommetrics-configuration-options)
  - [DataExpression – available data in `data`](docs/custom-metrics.md#dataexpression--available-data-in-data)

**Resource guides**

- [AKS Node Pool](docs/resources/aks-node-pool.md#aks-node-pool)
- [SQL Elastic Pool](docs/resources/sql-elastic-pool.md#sql-elastic-pool)
- [SQL Database](docs/resources/sql-database.md#sql-database)
  - [DTU Scaling Example](docs/resources/sql-database.md#dtu-scaling-example)
  - [MaxDataBytes Scaling Example](docs/resources/sql-database.md#maxdatabytes-scaling-example)
- [PostgreSQL Flexible Server](docs/resources/postgresql-flexible-server.md#postgresql-flexible-server)
- [MySQL Flexible Server](docs/resources/mysql-flexible-server.md#mysql-flexible-server)
- [Azure Files](docs/resources/azure-files.md#azure-files)
- [Azure DevOps Parallel Jobs](docs/resources/azure-devops-parallel-jobs.md#azure-devops-parallel-jobs)
  - [Prerequisites](docs/resources/azure-devops-parallel-jobs.md#prerequisites)
  - [Firewall Requirements](docs/resources/azure-devops-parallel-jobs.md#firewall-requirements)
  - [Resource ID Format](docs/resources/azure-devops-parallel-jobs.md#resource-id-format)
  - [Supported Dimensions](docs/resources/azure-devops-parallel-jobs.md#supported-dimensions)
  - [Custom Metrics](docs/resources/azure-devops-parallel-jobs.md#custom-metrics)
  - [Example Configuration](docs/resources/azure-devops-parallel-jobs.md#example-configuration)

**On this page**

- [Quick start](#quick-start)
- [Full documentation map](#documentation)

*Anchors match GitHub’s Markdown heading IDs (e.g. on github.com). If a link does not jump correctly in another viewer, use that viewer’s heading outline or search.*

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
| Azure SQL Elastic Pools | Dtu, PerDatabaseMaxCapacity, MaxDataBytes | | Per-database max defaults to pool DTU when no `PerDatabaseMaxCapacity` rule exists; see [SQL Elastic Pool](docs/resources/sql-elastic-pool.md). |
| Azure SQL Databases | Dtu, MaxDataBytes | | MaxDataBytes supports DTU and VCore models (see [SQL Database docs](docs/resources/sql-database.md)) |
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

You can build your own Docker images from the source code. However, Azure Autoscaler uses the **Business Source License (BSL) 1.1**, so usage is limited to what the license specifies. The BSL permits copying, modification, derivative works, redistribution, and **non-production use**. For production use, you must comply with the license terms—either through the Additional Use Grant (if any) or by purchasing a commercial license. See the [LICENSE](LICENSE) file for the full terms. The [Installation guide](docs/installation.md) has build instructions.

### Why Licensing?

The licensing model helps support the ongoing development and maintenance of Azure Autoscaler. By purchasing a license, you're directly contributing to:
- Continued feature development
- Bug fixes and security updates
- Documentation improvements
- Community support

This approach allows us to keep the source code open and freely available while ensuring sustainable development of the project.

## Quick start

The minimal path is Docker on your workstation. For Kubernetes, Terraform, or Azure Container Instances, follow the full [Installation guide](docs/installation.md).

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
            ScaleTarget: "(data) => (data.Metrics["FileCapacity"].Values.First().Average + 50).ToString()"
            DimensionValueCeilingStep: "1"  # Round up to nearest GB
            DimensionValueMin: "100"  # Minimum 100 GB for Azure Files
```

Start the image with Docker:

```powershell
docker compose up
```

## Documentation

| Topic | Description |
| --- | --- |
| [Installation](docs/installation.md) | Deploy with Docker locally, Kubernetes (Terraform snippets), or Azure Container Instances |
| [Configuration reference](docs/configuration.md) | `config.yml` structure: resources, metrics, forecasts, scaling rules |
| [Custom metrics](docs/custom-metrics.md) | `CustomMetrics` and `DataExpression` |
| [AKS Node Pool](docs/resources/aks-node-pool.md) | Example: node pools and VMSS metrics |
| [SQL Elastic Pool](docs/resources/sql-elastic-pool.md) | Example: pools, DTU, storage, **PerDatabaseMaxCapacity** |
| [SQL Database](docs/resources/sql-database.md) | DTU and `MaxDataBytes` examples |
| [PostgreSQL Flexible Server](docs/resources/postgresql-flexible-server.md) | Example configuration |
| [MySQL Flexible Server](docs/resources/mysql-flexible-server.md) | Example configuration |
| [Azure Files](docs/resources/azure-files.md) | Example configuration |
| [Azure DevOps Parallel Jobs](docs/resources/azure-devops-parallel-jobs.md) | Commerce API parallelism and metrics |
