## Why

Gather for scaling `custom_*` metrics and push for YAML `CustomMetrics` shared the `CustomMetric(s)` stem, which misled later work toward QUERY on the gather hook. This change disambiguates C# owners while keeping the operator YAML contract.

## What Changes

- Rename gather hook `CustomMetric` → `GatherScalingCustomMetric` and related helpers to `ScalingCustomMetric*` stem.
- Rename push stack types to `PublishedMetric*` (`PublishedMetricsPusher`, `PublishedMetricConfig`, eval context types).
- Bind YAML keys `CustomMetrics` / `CustomMetricsNamespace` to the new properties via configuration aliases (not **BREAKING** for operators).
- Update docs, tests, and devdocs to use the new vocabulary; delete debt file `knowledge/debt/2026-09-15-rename-custommetric-vs-custommetrics.md`.
- Unchanged: scaling metric names `custom_*`, QUERY on push rows only, no `SyntheticMetrics`.

## Capabilities

### New Capabilities

(none)

### Modified Capabilities

- `core_metrics_custom_query`: Requirement text references PublishedMetric push types; operator YAML remains `CustomMetrics`.

## Impact

C# across autoscaler, autoscalertests, docs/custom-metrics.md, knowledge/devdocs, sample configs, OpenSpec live spec wording.
