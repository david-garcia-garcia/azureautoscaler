# Rename `CustomMetric()` vs `CustomMetrics` stems

Human explicitly approved TAKING this previously-noted large debt and landing it in its own PR.

`CustomMetric()` on `ResourceState` is in-process gather for `Metrics` names that start with `custom_`. `CustomMetrics` on `Resource` is a push list evaluated by `CustomMetricsPusher`. They share a stem and are two jobs. QUERY belongs on the push unit.

This ticket IS the dedicated rename: disambiguate the two stems so later work does not attach QUERY/portal-mirror behavior to the gather hook.

Constraints:
- Do not invent a third `SyntheticMetrics` type.
- QUERY stays on the push/config unit, not on `ResourceState.CustomMetric()`.
- Public YAML `CustomMetrics`, docs, gather `custom_*` prefix, and Azure DevOps metric names are in the blast radius — explore/propose must name the rename map (old → new) before implement.
- Delete debt file `knowledge/debt/2026-09-15-rename-custommetric-vs-custommetrics.md` when the rename lands.

Context: `autoscaler/resourcemanagement/ResourceState.cs` `CustomMetric`, `autoscaler/configuration/Resource.cs` `CustomMetrics`, `autoscaler/metrics/AzureMonitorMetricsGatherer.cs` `custom_` prefix.
