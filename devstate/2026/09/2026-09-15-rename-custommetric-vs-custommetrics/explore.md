# Explore

## Concepts
- **Gather job**: `AzureMonitorMetricsGatherer` calls into `ResourceState` when a scaling `Metrics` entry name starts with `custom_` (today AzDO parallel jobs). Not QUERY, not YAML push list.
- **Push job**: `Resource.CustomMetrics` rows validated at startup, executed by `CustomMetricsPusher` (DataExpression and Query). Expression context is built via `BuildCustomMetricDataContextAsync` / `ICustomMetricDataProvider`.
- **Operator contract**: YAML keys `CustomMetrics` and root `CustomMetricsNamespace` are public; scaling metric **names** `custom_*` / `custom_azdo_*` stay as-is.

## Decisions
- Rename **C#** identifiers to two stems; keep YAML keys via `ConfigurationKeyName` aliases (no operator file migration).
- Gather stem: `ScalingCustomMetric` (method `GatherScalingCustomMetric`, helper `GetScalingCustomMetricExtraAsync`).
- Push stem: `PublishedMetric` (`PublishedMetrics`, `PublishedMetricConfig`, `PublishedMetricsPusher`, eval context types).
- Do not add `SyntheticMetrics`. QUERY remains on `PublishedMetricConfig` only.
- Delete `knowledge/debt/2026-09-15-rename-custommetric-vs-custommetrics.md` when implement lands.

### Rename map (old → new)

| Old | New | Notes |
|-----|-----|-------|
| `ResourceState.CustomMetric(...)` | `GatherScalingCustomMetric(...)` | gather hook |
| `GetCustomMetricExtraAsync` | `GetScalingCustomMetricExtraAsync` | gather helpers |
| `Resource.CustomMetrics` | `PublishedMetrics` | YAML alias `CustomMetrics` |
| `Configuration.CustomMetricsNamespace` | `PublishedMetricsNamespace` | YAML alias `CustomMetricsNamespace` |
| `CustomMetricConfig` | `PublishedMetricConfig` | type + file |
| `PrepareAndValidateCustomMetric` | `PrepareAndValidatePublishedMetric` | validation |
| `CustomMetricsPusher` | `PublishedMetricsPusher` | push owner |
| `CustomMetricDataContext` | `PublishedMetricEvalContext` | DataExpression context |
| `CustomMetricHelpers` | `PublishedMetricHelpers` | |
| `CustomMetricSeries` | `PublishedMetricSeries` | |
| `ICustomMetricDataProvider` | `IPublishedMetricEvalContextBuilder` | push-only interface |
| `BuildCustomMetricDataContextAsync` | `BuildPublishedMetricEvalContextAsync` | |
| Test `CustomMetricValues` / override | `ScalingCustomMetricValues` / `GatherScalingCustomMetric` | test double |

Unchanged: scaling metric names `custom_*`; YAML keys `CustomMetrics`; docs section title may still say Custom Metrics for operators.

## Open questions
- Q: Should live OpenSpec folder `core_metrics_custom_query` kebab id rename to match PublishedMetric vocabulary?
  Rank: structural incidental — many archive references; criterion is C# rename only
  Decision: assumed — keep spec id `core_metrics_custom_query`; update requirement text inside spec to say PublishedMetrics / YAML CustomMetrics.
  By: explore
