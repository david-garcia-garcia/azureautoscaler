# Requirement
IssueKey: 2026-09-15-rename-custommetric-vs-custommetrics

## Problem
Two different jobs share the `CustomMetric` / `CustomMetrics` stem: in-process gather for scaling metrics whose names start with `custom_`, versus YAML-driven push of operator metrics to Azure Monitor (including QUERY). The overlap invites wiring QUERY or portal-mirror logic onto the gather hook.

## Current (code)
- `autoscaler/resourcemanagement/ResourceState.cs` — virtual `CustomMetric(...)` throws by default; `BuildCustomMetricDataContextAsync` feeds push expressions.
- `autoscaler/metrics/AzureMonitorMetricsGatherer.cs` — dispatches `metric.Name.StartsWith("custom_")` to `state.CustomMetric(...)`.
- `autoscaler/resources/AzureDevops/AzureDevOpsParallelJobsResourceState.cs` — overrides `CustomMetric` for AzDO queue metrics (`custom_azdo_*` names in scaling config).
- `autoscaler/configuration/Resource.cs` — `List<CustomMetricConfig> CustomMetrics`.
- `autoscaler/configuration/CustomMetricConfig.cs`, `Configuration.PrepareAndValidateCustomMetric` — push row validation (Query XOR DataExpression).
- `autoscaler/metrics/CustomMetricsPusher.cs` — push path owner.
- `docs/custom-metrics.md`, `readme.md` — document `CustomMetrics` YAML.
- `knowledge/debt/2026-09-15-rename-custommetric-vs-custommetrics.md` — deferred rename note on `1.x`.

## Desired
Land the dedicated rename: publish an old→new map in explore/propose, then rename C# types and methods so gather vs push stems are distinct. Keep QUERY on the push unit only. Do not add `SyntheticMetrics`. Delete the debt file when done. Preserve operator YAML keys `CustomMetrics` / `CustomMetricsNamespace` (backward-compatible binding) unless propose documents an explicit migration.

## Affected
Resource states, metrics gatherer/pusher, configuration types, tests, docs, OpenSpec `core_metrics_custom_query`, devdocs `core_metrics_custom_query.md`, sample YAML under `autoscaler/config/`.

## Out of scope
Renaming scaling metric names (`custom_*`, including `custom_azdo_*`). Inventing `SyntheticMetrics`. SQL `CustomMetric()` implementation on SQL types. Live-spec folder kebab renames (separate debt).

## Unknowns
Exact target identifiers for gather vs push families (to be fixed in explore/propose rename map).

## Tensions
Human override: proceed with full workflow despite large blast radius (was `note large` on prior ticket).
