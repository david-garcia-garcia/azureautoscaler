## Why

The forecast baseline currently hard-codes `Max` as the aggregation across compatible historical days per (weekday, slot). With a 60-day lookback this means ~8 historical samples are collected but only the single peak ever influences the forecast, making it overly conservative and unable to reflect that recent weeks may be more representative of current load patterns.

## What Changes

- Add `ForecastBaselineAggregation` property to `Metric` config (values: `Max` | `Mean` | `WeightedMean`; default `Max` for backward compatibility).
- Add `ForecastBaselineDecayDays` property to `Metric` config (double, used only when `WeightedMean`; default 14.0 days half-life).
- Replace the hardcoded `contributors.Max(c => c.Value)` in `ComputeFullForecastFromHistory` with a call to a new aggregation helper that dispatches based on the configured mode.
- Update the diagnostic label from `max=<value>` to `agg=<value>` everywhere (baseline contributor lines).
- Pass `Metric` config (or extracted aggregation params) through `ComputeFullForecastFromHistory` so it can apply the configured mode.
- Add `ForecastBaselineSmoothingNeighbourWeight` property to `Metric` config (double?, default null = disabled). After cross-day aggregation, each baseline cell is blended with its immediate time-adjacent slots (s−1 and s+1 on the same weekday) using a downweighted average; the result replaces the raw aggregate **only if higher** (upward-only). Applied before `ForecastBaselineBoostFactor`.
- Add `ForecastBaselineBoostFactor` property to `Metric` config (double?, default null = 1.0 = no change). Multiplicative factor applied to each baseline cell after smoothing and before snap/anchor modes.

## Capabilities

### New Capabilities

- `forecast-baseline-aggregation`: Configurable aggregation strategy (`Max`, `Mean`, `WeightedMean`) for combining N historical same-weekday slot values into a single forecast cell, with recency decay support for `WeightedMean`.
- `forecast-baseline-smoothing`: Optional upward-only temporal smoothing of the per-weekday baseline grid, blending each slot with downweighted time-adjacent neighbours. Controlled by `ForecastBaselineSmoothingNeighbourWeight`.
- `forecast-baseline-boost`: Optional global post-aggregation multiplier (`ForecastBaselineBoostFactor`) applied per cell after smoothing and before snap/anchor modes.

### Modified Capabilities

- `forecast-feature`: The baseline computation contract changes — the projected cell value is no longer always the maximum; it is determined by the configured aggregation mode. Diagnostic output label changes from `max=` to `agg=`.

## Impact

- `autoscaler/configuration/Metric.cs` — four new optional properties (`ForecastBaselineAggregation`, `ForecastBaselineDecayDays`, `ForecastBaselineSmoothingNeighbourWeight`, `ForecastBaselineBoostFactor`).
- `autoscaler/metrics/MetricForecastService.cs` — aggregation logic in `ComputeFullForecastFromHistory`; new temporal smoothing pass; `ForecastBaselineBoostFactor` application; diagnostic formatting in `AppendFullWeeklyForecastLines`; method signature additions to thread config through.
- `autoscalertests/ForecastDiagnosticLinesTests.cs` — update assertions for `agg=` label and add tests for `Mean`, `WeightedMean`, smoothing, and boost modes.
- No breaking changes to YAML resource files (new properties are optional with defaults).
- No changes to snap/anchor modes; they consume the baseline output unchanged.
