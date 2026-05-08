## Context

The forecast baseline is built in `MetricForecastService.ComputeFullForecastFromHistory`. For each `(target weekday, slot)` pair, it collects N contributor values from affinity-matched historical daily windows and currently always takes `Max` over them. With a 60-day lookback and same-day affinity, N ≈ 8 comparable historical days exist per slot — enough for meaningful aggregation strategies, but the `Max` always picks the single worst week and ignores everything else.

The `ForecastSnapPercentile` config already exists for snap/anchor modes (post-baseline transform). This change is orthogonal — it controls the baseline itself.

## Goals / Non-Goals

**Goals:**
- Make the Level 2 (cross-day) baseline aggregation configurable per metric.
- Support three modes: `Max` (unchanged default), `Mean`, `WeightedMean` (exponential recency decay).
- Expose two new nullable properties on `Metric`: `ForecastBaselineAggregation` (string) and `ForecastBaselineDecayDays` (double?).
- Rename diagnostic label from `max=` to `agg=` to reflect the actual aggregation used.
- Keep backward compatibility: unset = `Max` behavior identical to today.
- Add optional upward-only temporal smoothing (`ForecastBaselineSmoothingNeighbourWeight`) that blends each aggregated slot with its time-adjacent neighbours, gently filling isolated dips without propagating peaks.
- Add optional post-aggregation boost factor (`ForecastBaselineBoostFactor`) as a simple per-cell multiplier applied after smoothing.

**Non-Goals:**
- Configuring Level 1 aggregation (within-day raw points → slot value; stays `Max`).
- Percentile mode (p50/p75/p90 etc.) — with typical N=2–8, percentile adds little over mean; can be added later.
- Changing snap/anchor modes or `ForecastSnapPercentile`.
- Automatic selection of mode based on N.
- Cross-day smoothing (blending across weekday boundaries).

## Decisions

### Decision 1: String enum on `Metric` rather than a delegate or a separate config class

**Chosen**: `ForecastBaselineAggregation: string` (values: `"Max"` | `"Mean"` | `"WeightedMean"`) and `ForecastBaselineDecayDays: double?`.

**Alternatives considered**:
- **Separate `ForecastBaselineConfig` sub-object**: cleaner but adds YAML nesting, breaking existing configs.
- **Delegate `Func<IList<ForecastBaselineContributor>, double>`**: not serializable; not suitable for YAML config.
- **Enum type on `Metric`**: cleaner in C# but requires a separate string property for YAML and custom parsing, same end result with more ceremony.

String property follows the same pattern already used for `ForecastMode` in `Metric`.

### Decision 2: WeightedMean uses exponential decay by `SampleDateUtc` age

Formula: `weight(c) = exp(-daysAgo / halfLife)` where `daysAgo = (endDate - c.SampleDateUtc).TotalDays` and `halfLife = ForecastBaselineDecayDays ?? 14.0`.

**Why exponential**: monotonically decreasing, smooth, has a single intuitive parameter (half-life). With `halfLife=14d` and a 60-day lookback, the most recent comparable Monday weighs roughly 8× the oldest one.

**Alternative**: linear decay — simpler but can over-penalise old points near the cutoff.

### Decision 3: Thread aggregation params through `ComputeFullForecastFromHistory` as value parameters (not pass full `Metric`)

`ComputeFullForecastFromHistory` is `internal` and used by tests. Passing the full `Metric` object would couple it to config; instead, extract the two values at the call site in `GetFullForecastAsync` / `ComputeForecastAsync` and pass `string baselineAggregation, double? baselineDecayDays`.

### Decision 4: Rename `max=` to `agg=` in diagnostics

One-time rename — `max=` is the hardcoded term in `AppendFullWeeklyForecastLines`. Renaming to `agg=` makes it accurate regardless of mode. No semantic break; this is a diagnostic string, not a contract.

### Decision 5: Temporal smoothing uses a downweighted neighbour blend, upward-only

**Chosen**: `ForecastBaselineSmoothingNeighbourWeight` (double?, default null = disabled). Each existing time-adjacent slot (s−1 and s+1 on the same projected weekday) contributes this weight; the center retains `1 − (n × weight)` where n is the number of existing neighbours (0, 1, or 2). The result is `max(raw, blended)` — replacing the raw aggregate only when the blend is higher.

Formula (with e.g. weight = 0.15, two neighbours exist):
```
blended(s) = 0.70 × agg(s) + 0.15 × agg(s−1) + 0.15 × agg(s+1)
cell(s)    = max(agg(s), blended(s))
```

**Why downweighted blend rather than hard max-neighbour**: a hard `max(s, s±1)` propagates genuine outlier peaks sideways unconditionally. A weighted blend with `weight ≪ 0.5` smooths gently; the upward-only gate means isolated dips beside consistent high-load slots are lifted while isolated spikes are not copied wholesale.

**Why time-only (same weekday, adjacent slot index)**: mixing weekday neighbours introduces weekday-semantics confusion (Mon ≠ Tue traffic pattern); time-adjacent slots on the same projected day are naturally correlated in load.

**Edge handling**: slots at index 0 and `slotsPerDay − 1` have only one neighbour. No midnight wrap; the day boundary is not crossed.

**Valid range**: `0 < weight < 0.5`. Values ≤ 0 disable smoothing (treated as null). Values ≥ 0.5 are clamped to 0.49 to ensure the center always retains majority weight.

**Order**: applied **after** `AggregateContributors`, **before** `ForecastBaselineBoostFactor`.

**Disabled by default** (`null` / 0): backward-compatible — no smoothing is applied.

### Decision 6: Post-aggregation boost factor is a simple per-cell multiplier

**Chosen**: `ForecastBaselineBoostFactor` (double?, default null = 1.0). Applied per cell after temporal smoothing and before snap/anchor modes. Values ≤ 0, NaN, or infinity are treated as 1.0.

**Why multiplicative rather than additive**: additive headroom depends on scale of the metric (different for eDTU 0-400 vs percent 0-100); a factor is dimensionless and composable.

**Stacking note**: operators should be aware that boost, smoothing, and a high percentile snap can all compound. The diagnostic line explicitly lists both the active mode and the boost factor.

## Risks / Trade-offs

- **Lower baseline than today** when switching from `Max` to `Mean`/`WeightedMean` on existing metrics → capacity may be under-provisioned during first forecast window.  
  → Mitigation: default stays `Max`; operator must explicitly opt in per metric.

- **N sensitivity**: with only N=2 contributors (short lookback, same-day only affinity), `WeightedMean` and `Mean` collapse toward each other and may underfit a genuine spike.  
  → Mitigation: document that modes other than `Max` are most useful with a lookback ≥ 30d.

- **`SampleDateUtc` accuracy**: `WeightedMean` relies on `WindowDateUtc` being set correctly in `BuildDailyWindows`. This was added in the previous session; verify it is populated before shipping.

- **Compounding headroom**: smoothing + boost + high snap percentile can each add margin independently. Operators should test the combined effect; the diagnostic line lists all active parameters explicitly to aid audit.

- **Smoothing boundary assumption**: `ApplyTemporalSmoothing` treats each weekday grid independently (no midnight wrap). If the metric has significant load that straddles midnight, the last and first slots of the day may benefit less from smoothing than mid-day slots. Acceptable for the current use case.

## Migration Plan

- Config changes are purely additive; no YAML migration needed.
- Default `ForecastBaselineAggregation = null / "Max"` → identical behavior to current.
- To adopt: add `ForecastBaselineAggregation: WeightedMean` (and optionally `ForecastBaselineDecayDays: 21`) to the metric in the resource YAML.
- Rollback: remove the two properties from YAML or set to `Max`.
