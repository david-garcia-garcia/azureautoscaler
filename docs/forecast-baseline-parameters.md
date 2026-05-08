# Forecast baseline parameters

When `ForecastEnable: true` is set on a metric, the autoscaler builds a **baseline grid**: one projected value per weekday × time-slot combination, derived from historical observations. The four parameters on this page let you control how that grid is computed before it reaches any `ForecastMode` transform (Anchors, AnchorWindow, Snap, etc.).

These parameters are all optional. Omitting them leaves behavior identical to legacy defaults.

## Quick reference

| Property | Type | Default | Summary |
|---|---|---|---|
| `ForecastBaselineAggregation` | `string` | `null` (= `Max`) | How to combine historical samples for the same weekday + slot. |
| `ForecastBaselineDecayDays` | `double?` | `14` (days) | Recency bias for `WeightedMean`: how quickly older samples lose influence. |
| `ForecastBaselineSmoothingNeighbourWeight` | `double?` | `null` (disabled) | Blends each slot with its immediate time-neighbours to fill isolated low-demand dips. |
| `ForecastBaselineBoostFactor` | `double?` | `1.0` (no change) | Uniform multiplier applied to every baseline cell after smoothing. |

## Processing pipeline

Each baseline cell goes through these steps in order:

```
historical samples
        │
        ▼
1. Cross-day aggregation      ← ForecastBaselineAggregation
        │                        ForecastBaselineDecayDays (WeightedMean only)
        ▼
2. Temporal smoothing         ← ForecastBaselineSmoothingNeighbourWeight
        │
        ▼
3. Boost                      ← ForecastBaselineBoostFactor
        │
        ▼
4. Snap / anchor transform    ← ForecastMode, ForecastSnapPercentile, etc.
```

Each parameter only affects its own stage. Boost does not influence smoothing; smoothing does not influence aggregation.

> **Tip:** Turn one knob at a time and read the diagnostic lines to confirm the effective values before adjusting the next parameter.

---

## Parameter reference

### `ForecastBaselineAggregation`

Controls how the N historical same-weekday slot values are combined into a single baseline cell.

| Value | Behaviour |
|---|---|
| `Max` *(default when null)* | Peak value across all matching historical slots. Most conservative; preserves rare spikes. |
| `Mean` | Arithmetic average. Reduces the baseline when spikes are infrequent. |
| `WeightedMean` | Average weighted by recency — recent samples count more. Controlled by `ForecastBaselineDecayDays`. |

```yaml
ForecastBaselineAggregation: WeightedMean
```

---

### `ForecastBaselineDecayDays`

Only used when `ForecastBaselineAggregation: WeightedMean`. Sets the exponential decay time constant **τ** (in days) that controls how quickly older samples lose relative influence.

Each historical sample's weight is `exp(−daysAgo / τ)`, where `daysAgo` is how many days ago that sample occurred relative to the forecast target date.

**What τ means in practice:**

| τ (days) | Age at which a sample has ~50% weight of today's sample | Age at which it has ~10% weight |
|---|---|---|
| 7 | ~5 days | ~16 days |
| **14** *(default)* | **~10 days** | **~32 days** |
| 21 | ~15 days | ~48 days |

- **Smaller τ** → the baseline closely tracks the most recent week or two; older history is largely ignored.
- **Larger τ** → older samples remain influential; the baseline is more stable over time.
- Values ≤ 0 are treated as the default (14 days).

```yaml
ForecastBaselineAggregation: WeightedMean
ForecastBaselineDecayDays: 21
```

---

### `ForecastBaselineSmoothingNeighbourWeight`

Smooths isolated low-demand slots by blending each slot with its immediate time-adjacent neighbours (`s−1` and `s+1` on the same weekday).

**Key behaviours:**

- **Upward-only**: smoothing can only raise a cell, never lower it. If the blended value is less than the raw aggregate, the raw value is kept.
- **Day boundary is not crossed**: the first slot of the day has no `s−1` neighbour; the last slot has no `s+1` neighbour.
- **Valid range**: `0 < w < 0.5`. Values ≥ 0.5 are clamped to `0.49` so the centre slot always retains majority weight. Null, 0, or negative values disable smoothing.

**Blend formula** (for a slot with `n` neighbours present, where `n` is 1 or 2):

```
blended(s) = (1 − n × w) × agg(s)  +  w × sum(agg(neighbours))
cell(s)    = max(agg(s), blended(s))
```

A value around **0.10–0.15** is a safe starting point. Use this when diagnostic output shows single-slot "holes" (one low slot surrounded by high-traffic slots).

```yaml
ForecastBaselineSmoothingNeighbourWeight: 0.12
```

---

### `ForecastBaselineBoostFactor`

Multiplies every baseline cell by a fixed factor **after** smoothing and **before** any `ForecastMode` snap or anchor transform. Use it to add a simple global safety margin.

- `1.10` → all cells are 10% higher than the smoothed baseline.
- Null, NaN, infinite, or ≤ 0 values are all treated as `1.0` (no change).

```yaml
ForecastBaselineBoostFactor: 1.15
```

**Boost vs `ForecastSnapPercentile`**

Both add headroom, but in different ways:

- `ForecastBaselineBoostFactor` scales every cell **uniformly** by a percentage, regardless of variance. It is simple and predictable.
- `ForecastSnapPercentile` summarises the spread of values **within each snap window**. It adds more margin in windows with high variance and less in stable ones.

Use boost for a flat global allowance. Use a higher snap percentile when you want extra margin to track how variable the load is within each window.

---

## Tuning guide

Start from the simplest configuration and add complexity only when you see a concrete need in the diagnostic output.

1. **Start with `Max` and nothing else.** Establish that the forecast is safe and conservative.

2. **Switch to `WeightedMean` with `ForecastBaselineDecayDays: 21`** if recent load trends should carry more weight than older spikes. The default τ of 14 days is already fairly recency-biased; 21 days is a good middle ground.

3. **Add smoothing around `0.12`** if the baseline has isolated low slots between high-traffic periods that cause unnecessary scale-down/scale-up cycles.

4. **Add a small boost (`1.05–1.15`)** if you want extra capacity margin without touching the snap or anchor configuration.

5. **Check diagnostic lines** after each change — they report the effective aggregation mode, smoothing weight (when active), and boost factor applied.

---

See also: XML `<remarks>` on each of the four properties in `Metric.cs` for quick cross-references while browsing the code.
