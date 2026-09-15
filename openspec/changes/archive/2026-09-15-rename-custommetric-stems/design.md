## Context

Two jobs today: (1) `AzureMonitorMetricsGatherer` → `ResourceState.CustomMetric` for `custom_*` scaling names; (2) `CustomMetricsPusher` + `CustomMetricConfig` for monitor publication including Query. Debt note tracked the clash; QUERY work kept push on the list unit.

## Goals / Non-Goals

**Goals:** Clear C# stems; YAML backward compatibility; remove debt file.

**Non-Goals:** Rename `custom_*` metric strings; rename OpenSpec folder `core_metrics_custom_query`; implement SQL gather hook.

## Decisions

### D1: Two stems in code, YAML unchanged

- Gather: `GatherScalingCustomMetric`, `GetScalingCustomMetricExtraAsync`.
- Push: `PublishedMetrics`, `PublishedMetricConfig`, `PublishedMetricsPusher`, `PublishedMetricEvalContext`, `IPublishedMetricEvalContextBuilder`.
- `[ConfigurationKeyName("CustomMetrics")]` and `[ConfigurationKeyName("CustomMetricsNamespace")]` on renamed properties.

### D2: No third config type

Reject `SyntheticMetrics`; Query stays on `PublishedMetricConfig`.

## Risks / Trade-offs

- Large diff touching tests and docs — mitigated by mechanical rename and unchanged YAML samples.
- External docs referencing old C# type names — devdocs updated in same PR.

## Migration Plan

Operators: none (YAML keys unchanged). Developers: follow rename map in `devstate/explore.md`.

## Open Questions

None blocking; live spec kebab id stays `core_metrics_custom_query` (explore assumed).
