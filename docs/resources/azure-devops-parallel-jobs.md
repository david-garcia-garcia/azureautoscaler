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

