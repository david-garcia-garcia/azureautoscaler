## 1. Configuration and types

- [x] 1.1 Rename `CustomMetricConfig` → `PublishedMetricConfig`; add YAML aliases on `PublishedMetrics` / `PublishedMetricsNamespace`
- [x] 1.2 Rename validation helper to `PrepareAndValidatePublishedMetric`

## 2. Push path

- [x] 2.1 Rename `CustomMetricsPusher` → `PublishedMetricsPusher` and DTO types to `PublishedMetric*`
- [x] 2.2 Rename `ICustomMetricDataProvider` → `IPublishedMetricEvalContextBuilder` and `BuildPublishedMetricEvalContextAsync`

## 3. Gather path

- [x] 3.1 Rename `CustomMetric` → `GatherScalingCustomMetric`; `GetScalingCustomMetricExtraAsync`; update gatherer and AzDO override

## 4. Docs and debt

- [x] 4.1 Update docs/custom-metrics.md, readme, devdocs, live spec merge targets
- [x] 4.2 Delete `knowledge/debt/2026-09-15-rename-custommetric-vs-custommetrics.md`; close issues row

## 5. Tests

- [x] 5.1 Rename test types/helpers; run autoscalertests
