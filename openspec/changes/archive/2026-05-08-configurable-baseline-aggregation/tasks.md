## 1. Configuration model

- [x] 1.1 Add `ForecastBaselineAggregation` string property to `Metric` (with XML doc: accepted values `Max` | `Mean` | `WeightedMean`, default null = Max)
- [x] 1.2 Add `ForecastBaselineDecayDays` double? property to `Metric` (with XML doc: half-life in days for WeightedMean; default 14.0 when unset)

## 2. Aggregation logic

- [x] 2.1 Add private static helper `AggregateContributors(IList<ForecastBaselineContributor> contributors, string? mode, double? decayDays, DateTimeOffset endDate) → double` in `MetricForecastService` implementing Max / Mean / WeightedMean dispatch
- [x] 2.2 Add `ResolveBaselineDecayDays(double? value) → double` static helper that clamps ≤ 0 to 14.0
- [x] 2.3 Add two parameters `string? baselineAggregation, double? baselineDecayDays` to `ComputeFullForecastFromHistory` signature
- [x] 2.4 Replace `contributors.Max(c => c.Value)` with a call to `AggregateContributors(...)` passing the new params
- [x] 2.5 Thread the two new params from `GetFullForecastAsync` → `ComputeFullForecastFromHistory` (read from `metric.ForecastBaselineAggregation` / `metric.ForecastBaselineDecayDays`)
- [x] 2.6 Thread the two new params from `ComputeForecastAsync` → `ComputeFullForecastFromHistory`

## 3. Diagnostic output

- [x] 3.1 Rename `max=` to `agg=` in `AppendFullWeeklyForecastLines` contributor line format string
- [x] 3.2 Update the static description comment on that diagnostic line to say "configured aggregation" instead of "max over affinity-matched historical days"

## 4. Tests (aggregation)

- [x] 4.1 Update existing `ForecastDiagnosticLinesTests` assertions that check `max=` → `agg=`
- [x] 4.2 Add test: `Mean` mode returns arithmetic average of contributors
- [x] 4.3 Add test: `WeightedMean` with two contributors of different ages — verify more-recent one has higher weight (result closer to recent value)
- [x] 4.4 Add test: single contributor returns same value regardless of mode
- [x] 4.5 Add test: `WeightedMean` with `ForecastBaselineDecayDays <= 0` falls back to 14.0 days (effectively behaves like a valid weighted mean, not NaN/zero)
- [x] 4.6 Add test: unset `ForecastBaselineAggregation` (null) behaves identically to `Max`

## 5. Boost factor

- [x] 5.1 Add `ForecastBaselineBoostFactor` double? property to `Metric` (with XML doc: multiplier applied to each baseline cell after aggregation and smoothing; default null = 1.0 = no change)
- [x] 5.2 Add `ResolveForecastBaselineBoostFactor(double? value) → double` static helper that clamps null/NaN/infinity/≤0 to 1.0
- [x] 5.3 Add `baselineBoostFactor` parameter to `ComputeFullForecastFromHistory` and apply it per cell after `AggregateContributors` (and after smoothing once implemented)
- [x] 5.4 Thread `metric.ForecastBaselineBoostFactor` from `GetFullForecastAsync` and `ComputeForecastAsync` into `ComputeFullForecastFromHistory`
- [x] 5.5 Populate `BaselineBoostFactorApplied` in `MetricForecastResult` and include it in the baseline mode diagnostic line
- [x] 5.6 Add test: `ForecastBaselineBoostFactor` multiplies each baseline cell by the configured factor after aggregation

## 6. Temporal smoothing

- [x] 6.1 Add `ForecastBaselineSmoothingNeighbourWeight` double? property to `Metric` (with XML doc: weight per adjacent time-slot for upward-only smoothing; default null = disabled; valid range 0–0.49)
- [x] 6.2 Add `ResolveSmoothingNeighbourWeight(double? value) → double` static helper that returns 0.0 for null/NaN/≤0 and clamps ≥0.5 to 0.49
- [x] 6.3 Add private static helper `ApplyTemporalSmoothing(double[] slotValues, double neighbourWeight) → double[]` that for each slot s computes `max(raw, (1 − n×w)×raw + w×sum(neighbours))` where n is the number of existing neighbours (0–2); does not cross the day boundary
- [x] 6.4 Add `baselineSmoothingNeighbourWeight` parameter to `ComputeFullForecastFromHistory`; call `ApplyTemporalSmoothing` on the per-slot aggregation array **before** applying `ForecastBaselineBoostFactor`
- [x] 6.5 Thread `metric.ForecastBaselineSmoothingNeighbourWeight` from `GetFullForecastAsync` and `ComputeForecastAsync` into `ComputeFullForecastFromHistory`
- [x] 6.6 Populate `BaselineSmoothingNeighbourWeightApplied` (double) in `MetricForecastResult` and include it in the baseline mode diagnostic line (emit only when > 0)
- [x] 6.7 Add test: smoothing lifts a dip slot when its neighbour aggregates are higher (result > raw aggregate for that slot)
- [x] 6.8 Add test: smoothing does not lower a slot that is already the local maximum
- [x] 6.9 Add test: edge slot (first or last of day) uses exactly one neighbour
- [x] 6.10 Add test: neighbour weight ≥ 0.5 is clamped to 0.49
- [x] 6.11 Add test: null/0 neighbour weight leaves all cells unchanged (smoothing disabled)

## 7. Documentation

- [x] 7.1 Add a `docs/forecast-baseline-parameters.md` reference page that documents all forecast baseline parameters in one place: `ForecastBaselineAggregation`, `ForecastBaselineDecayDays`, `ForecastBaselineSmoothingNeighbourWeight`, `ForecastBaselineBoostFactor`. For each parameter include: type, default, accepted values/range, when to use it, and a concrete YAML example.
- [x] 7.2 Add a "parameter interaction and ordering" section to `docs/forecast-baseline-parameters.md` that explains the processing pipeline — aggregation → smoothing → boost → snap/anchor — so operators understand how values compound.
- [x] 7.3 Add a "tuning guide" section with recommended starting values and trade-off notes: e.g. when `WeightedMean` is better than `Max`, what smoothing weight feels safe (e.g. 0.10–0.15), when to use `ForecastBaselineBoostFactor` vs raising `ForecastSnapPercentile`.
- [x] 7.4 Add inline XML `<remarks>` or `<para>` blocks to each of the four new properties in `Metric.cs` cross-referencing the other related properties so operators can discover the full parameter set from code intellisense.
