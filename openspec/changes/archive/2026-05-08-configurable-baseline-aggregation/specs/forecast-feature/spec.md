## ADDED Requirements

### Requirement: Baseline diagnostic output labels the aggregated cell value
The forecast diagnostic lines SHALL label the aggregated baseline cell value as `agg=<value>` (replacing the previous `max=<value>` label) to accurately reflect the configured aggregation mode.

#### Scenario: Diagnostic line uses agg= label regardless of mode
- **WHEN** diagnostic lines are produced for any baseline aggregation mode (Max, Mean, or WeightedMean)
- **THEN** each contributor line SHALL use the format `agg=<value>  [<contributors>]`
- **THEN** the label SHALL NOT read `max=` even when the aggregation mode is Max

#### Scenario: Contributor bracket content is unchanged
- **WHEN** contributor diagnostic lines are produced
- **THEN** the bracket content format `[62, !71, 45]` (with `!` for capped values) SHALL remain identical to the previous format
