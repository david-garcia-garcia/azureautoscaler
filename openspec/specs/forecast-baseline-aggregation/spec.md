# forecast-baseline-aggregation Specification

## Purpose
TBD - created by archiving change configurable-baseline-aggregation. Update Purpose after archive.
## Requirements
### Requirement: Baseline aggregation mode is configurable per metric
The system SHALL support a `ForecastBaselineAggregation` property on the `Metric` configuration that controls how N historical same-weekday slot values are combined into the forecast cell value.
Accepted values: `Max` (default), `Mean`, `WeightedMean`.
When unset or `null`, the system SHALL behave identically to `Max`.

#### Scenario: Default mode uses Max
- **WHEN** `ForecastBaselineAggregation` is not set (null or absent from YAML)
- **THEN** the forecast cell value SHALL equal the maximum contributor value, identical to legacy behavior

#### Scenario: Mean mode averages all contributors
- **WHEN** `ForecastBaselineAggregation` is `Mean`
- **THEN** the forecast cell value SHALL equal the arithmetic mean of all contributor values for that (weekday, slot)

#### Scenario: WeightedMean applies recency decay
- **WHEN** `ForecastBaselineAggregation` is `WeightedMean`
- **THEN** each contributor SHALL be weighted by `exp(-daysAgo / halfLife)` where `daysAgo` is `(endDate - contributor.SampleDateUtc).TotalDays` and `halfLife` is `ForecastBaselineDecayDays ?? 14.0`
- **THEN** the forecast cell value SHALL equal `sum(value * weight) / sum(weight)`

#### Scenario: WeightedMean default half-life is 14 days
- **WHEN** `ForecastBaselineAggregation` is `WeightedMean` and `ForecastBaselineDecayDays` is not set
- **THEN** a half-life of 14.0 days SHALL be used

#### Scenario: Custom decay half-life is respected
- **WHEN** `ForecastBaselineAggregation` is `WeightedMean` and `ForecastBaselineDecayDays` is set to a positive value (e.g. 21.0)
- **THEN** that value SHALL be used as the exponential decay half-life in days

#### Scenario: WeightedMean with equal-aged contributors equals Mean
- **WHEN** all contributors for a slot share the same `SampleDateUtc`
- **THEN** `WeightedMean` result SHALL equal the arithmetic mean of their values

#### Scenario: Single contributor always returns its own value regardless of mode
- **WHEN** exactly one contributor exists for a (weekday, slot)
- **THEN** all aggregation modes SHALL return that contributor's value

### Requirement: ForecastBaselineDecayDays clamps to a positive value
The system SHALL ignore non-positive `ForecastBaselineDecayDays` values and fall back to the default half-life of 14.0 days to avoid division-by-zero or inverted weights.

#### Scenario: Zero or negative decay days fall back to default
- **WHEN** `ForecastBaselineDecayDays` is set to 0 or a negative number
- **THEN** the system SHALL use 14.0 days as the effective half-life

---

### Requirement: Temporal smoothing blends each slot with downweighted time-adjacent neighbours (upward-only)
The system SHALL support a `ForecastBaselineSmoothingNeighbourWeight` property on the `Metric` configuration.
When set to a positive value, after cross-day aggregation the system SHALL blend each baseline cell with its immediate time-adjacent slots (s−1 and s+1 on the same projected weekday), and replace the raw aggregate **only if the blend is strictly higher**.

The blend formula for slot s (with n existing neighbours where n ∈ {0, 1, 2}):
```
blended(s) = (1 − n × weight) × agg(s) + weight × sum(agg(neighbour_i))
cell(s)    = max(agg(s), blended(s))
```

The first and last slots of the day have exactly one neighbour each. The day boundary is **not** crossed (slot 0 has no slot −1; the last slot has no slot+N).

When null or ≤ 0, smoothing is disabled and the aggregated value is used unchanged.

#### Scenario: Smoothing is disabled by default
- **WHEN** `ForecastBaselineSmoothingNeighbourWeight` is null or absent from YAML
- **THEN** each baseline cell SHALL equal the raw cross-day aggregate, unchanged

#### Scenario: Smoothing lifts a dip beside a high-load neighbour slot
- **WHEN** `ForecastBaselineSmoothingNeighbourWeight` is a positive value and a slot's aggregate is lower than its neighbours
- **THEN** the smoothed cell value SHALL be strictly greater than the raw aggregate

#### Scenario: Smoothing never lowers a cell below its raw aggregate
- **WHEN** `ForecastBaselineSmoothingNeighbourWeight` is set and a slot's aggregate is already the local maximum
- **THEN** `cell(s) = agg(s)` — the raw aggregate is returned unchanged

#### Scenario: Edge slots use a single neighbour
- **WHEN** the slot is at the first or last position of the day
- **THEN** only the one existing adjacent slot is used (n = 1); the day boundary is not crossed

#### Scenario: Neighbour weight is clamped to a valid range
- **WHEN** `ForecastBaselineSmoothingNeighbourWeight` is ≥ 0.5
- **THEN** the system SHALL clamp the effective weight to 0.49 so the center always retains majority weight

---

### Requirement: ForecastBaselineBoostFactor multiplies baseline cells after smoothing
The system SHALL support a `ForecastBaselineBoostFactor` property on the `Metric` configuration.
When set to a positive finite value other than 1.0, each baseline cell SHALL be multiplied by this factor **after** temporal smoothing and **before** snap/anchor modes.
When null, NaN, infinite, or ≤ 0, the factor SHALL be treated as 1.0 (no change).

#### Scenario: Boost factor scales all cells uniformly
- **WHEN** `ForecastBaselineBoostFactor` is set to e.g. 1.20
- **THEN** every baseline cell SHALL equal `smoothed_cell × 1.20`

#### Scenario: Default behavior when boost is unset
- **WHEN** `ForecastBaselineBoostFactor` is null or absent from YAML
- **THEN** each cell SHALL be unchanged (factor = 1.0)

#### Scenario: Boost is applied after smoothing and before snap
- **WHEN** both `ForecastBaselineSmoothingNeighbourWeight` and `ForecastBaselineBoostFactor` are set
- **THEN** smoothing SHALL be computed first on the raw aggregates, then the boost factor SHALL be applied to each smoothed cell

---

### Requirement: All baseline parameters are documented in a single reference page
The project SHALL provide a `docs/forecast-baseline-parameters.md` reference page that covers all four forecast baseline parameters (`ForecastBaselineAggregation`, `ForecastBaselineDecayDays`, `ForecastBaselineSmoothingNeighbourWeight`, `ForecastBaselineBoostFactor`) with type, default, accepted values/range, when to use each, and YAML examples.

#### Scenario: Processing pipeline is explained
- **WHEN** an operator reads the documentation
- **THEN** the docs SHALL clearly explain the end-to-end processing order: aggregation → smoothing → boost → snap/anchor, so the compounding effect of combining multiple parameters is unambiguous

#### Scenario: Tuning guidance is provided
- **WHEN** an operator is deciding which mode or values to use
- **THEN** the docs SHALL include a tuning guide with recommended starting values and notes on trade-offs (e.g. when `WeightedMean` is preferable over `Max`, safe smoothing weight range, boost vs snap percentile)

#### Scenario: Code intellisense surfaces cross-references
- **WHEN** an operator browses `Metric.cs` properties in an IDE
- **THEN** each of the four new properties SHALL have XML doc remarks that reference the other related properties, allowing discovery of the full parameter set without leaving the code

