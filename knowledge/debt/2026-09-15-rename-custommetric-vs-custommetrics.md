# Rename `CustomMetric()` vs `CustomMetrics` stems

IssueKey: 2026-09-15-sql-query-synthetic-metrics
Size: large
Action: note

## Why this follow-up

`CustomMetric()` on `ResourceState` is in-process gather for `Metrics` names that start with `custom_`. `CustomMetrics` on `Resource` is a push list evaluated by `CustomMetricsPusher`. They share a stem and are two jobs. QUERY belongs on the push unit; folding it into `CustomMetric()` or inventing `SyntheticMetrics` would hide the owner.

## Why it was not taken

Public config (`CustomMetrics` in YAML), docs (`docs/custom-metrics.md`), gather dispatch (`custom_*`), and Azure DevOps metric names already use these stems. Blast radius is large. This change only notes the clash.

## Risks

Later work keeps attaching QUERY or portal-mirror behavior to `CustomMetric()` because the name looks like the hook, or adds a third “synthetic” type beside both.

## Context

Current: `autoscaler/resourcemanagement/ResourceState.cs` `CustomMetric`, `autoscaler/configuration/Resource.cs` `CustomMetrics`, `autoscaler/metrics/AzureMonitorMetricsGatherer.cs` `custom_` prefix.
Proposed: keep both until a dedicated rename; new QUERY fields go on `CustomMetricConfig`.
