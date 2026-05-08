# Forecast baseline parameters

When `ForecastEnable: true` is set on a metric, the autoscaler builds a **baseline grid**: one projected value per weekday × time-slot combination, derived from historical observations. Before that grid reaches any `ForecastMode` transform (Anchors, AnchorWindow, Snap) it passes through a fixed pipeline of steps, each controlled by a dedicated set of parameters.

All parameters on this page are optional. Omitting them leaves behaviour identical to the legacy defaults.

## Processing pipeline

```
historical raw time series
        │
        ▼
1. Day selection               ← ForecastAffinitySameDayFactor
        │                         ForecastAffinityWeekdayFactor
        │                         ForecastAffinityWeekendFactor
        ▼
2. Capped-point correction     ← ForecastMetricMax
        │                         ForecastCappedCorrectionThreshold
        │                         ForecastCappedCorrectionFactor
        ▼
3. Cross-day aggregation       ← ForecastBaselineAggregation
        │                         ForecastBaselineDecayDays  (WeightedMean only)
        ▼
4. Temporal smoothing          ← ForecastBaselineSmoothingNeighbourWeight
        │
        ▼
5. Boost                       ← ForecastBaselineBoostFactor
        │
        ▼
6. Snap / anchor transform     ← ForecastMode, ForecastSnapPercentile, …
                                  (documented in configuration.md)
```

Each step only sees the output of the previous one. Boost does not influence smoothing; aggregation does not influence day selection.

> **Tip:** change one parameter at a time and read the diagnostic lines to confirm the effective values before adjusting the next one.

---

## Step 1 — Day selection (affinity)

### How the baseline grid is built

The autoscaler scans `ForecastTimeRange` worth of history and slices it into **daily windows** — one per calendar day. Each day is subdivided into fixed-width **time slots** (`ForecastSlotMinutes`, default 60 min), so a day with 60-minute slots produces 24 slots numbered 0–23.

The result is a **7 × N grid** (7 weekdays × N slots per day). For every cell in that grid — say, _Wednesday slot 10 (10:00–11:00)_ — the autoscaler needs to decide which historical days can **contribute** a value to that cell.

That is what affinity does: it decides which past calendar days are eligible contributors for each target weekday column.

### How affinity works

Each historical day is assigned a score against the target weekday:

| Property | Default | When it applies |
|---|---|---|
| `ForecastAffinitySameDayFactor` | `1.0` | Historical day is the **same** weekday as the target (e.g. past Monday → target Monday). |
| `ForecastAffinityWeekdayFactor` | `0.3` | Both are weekdays but **different** (e.g. past Tuesday → target Wednesday). |
| `ForecastAffinityWeekendFactor` | `0.3` | Both are weekend days but **different** (e.g. past Sunday → target Saturday). |

A weekday–weekend pair always scores `0` and is never eligible, regardless of settings.

A historical day is included as a contributor **only if its score is strictly greater than 0.5**. This is a binary gate — the score is not used as a weight inside the aggregation (Step 3 handles weights).

**With the defaults, only exact same-weekday history is ever used.** The default cross-day scores of `0.3` are below the threshold, so past Tuesdays never contribute to the Wednesday forecast. To allow cross-day borrowing, raise the relevant factor above `0.5`:

```yaml
# Include all past weekdays when forecasting any weekday target
ForecastAffinityWeekdayFactor: 0.8
```

### What happens per cell

Once compatible days are known for a target weekday, each cell value is computed independently:

```
cell(Wednesday, slot 10) =
    aggregate(
        all compatible historical days that have a value at slot 10
    )
```

If a compatible day has no data at that slot (e.g. the resource was offline), it simply does not contribute to that cell. Cells with no contributors at all are omitted from the grid.

---

## Step 2 — Capped-point correction

If a metric is frequently near its maximum capacity, raw observations may be artificially low — the resource was at the ceiling, not at actual demand. Before aggregation, the autoscaler can detect and inflate those points.

`ForecastMetricMax` names another metric (from the same `Metrics` map) that represents available capacity at each point in time. When set, each raw data point is tested against the corresponding capacity sample.

| Property | Default | Meaning |
|---|---|---|
| `ForecastMetricMax` | *(none)* | Name of the metric that represents available capacity. Required to enable capped-point detection. |
| `ForecastCappedCorrectionThreshold` | `0.95` | If `observed / capacity ≥ threshold`, the point is classified as likely capped. |
| `ForecastCappedCorrectionFactor` | `1.2` | Multiplier applied to capped points. `1.2` → +20%. |

Example: with threshold `0.95` and factor `1.2`:

- `98 / 100` → ratio `0.98 ≥ 0.95` → capped → corrected value `98 × 1.2 = 117.6`
- `80 / 100` → ratio `0.80 < 0.95` → not capped → value unchanged

```yaml
ForecastMetricMax: total_capacity
ForecastCappedCorrectionThreshold: 0.95
ForecastCappedCorrectionFactor: 1.2
```

---

## Step 3 — Cross-day aggregation

After day selection and capped-point correction, the selected historical contributors for each `(weekday, slot)` are combined into one baseline cell.

### `ForecastBaselineAggregation`

| Value | Behaviour |
|---|---|
| `Max` *(default when null)* | Peak value across all contributors. Most conservative; preserves rare spikes. |
| `Mean` | Arithmetic average. Reduces the baseline when spikes are infrequent. |
| `WeightedMean` | Average weighted by recency — recent contributors count more. Controlled by `ForecastBaselineDecayDays`. |

```yaml
ForecastBaselineAggregation: WeightedMean
```

### `ForecastBaselineDecayDays`

Only used when `ForecastBaselineAggregation: WeightedMean`. Sets the exponential decay time constant **τ** (days): each contributor's weight is `exp(−daysAgo / τ)`, where `daysAgo` is how many days ago that sample occurred relative to the forecast target date.

| τ (days) | Age at ~50% weight of today | Age at ~10% weight |
|---|---|---|
| 7 | ~5 days | ~16 days |
| **14** *(default)* | **~10 days** | **~32 days** |
| 21 | ~15 days | ~48 days |

- **Smaller τ** → closely tracks the most recent week or two; older history is largely ignored.
- **Larger τ** → older samples remain influential; the baseline is more stable over time.
- Values ≤ 0 are treated as the default (14 days).

```yaml
ForecastBaselineAggregation: WeightedMean
ForecastBaselineDecayDays: 21
```

---

## Step 4 — Temporal smoothing

Smooths isolated low-demand slots by blending each slot with its immediate time-adjacent neighbours (`s−1` and `s+1`) on the same projected weekday.

### `ForecastBaselineSmoothingNeighbourWeight`

**Key behaviours:**

- **Upward-only**: smoothing can only raise a cell, never lower it. If the blended value is less than the raw aggregate, the raw value is kept.
- **Day boundary is not crossed**: the first slot of the day has no `s−1` neighbour; the last slot has no `s+1` neighbour.
- **Valid range**: `0 < w < 0.5`. Values ≥ 0.5 are clamped to `0.49` so the centre slot always retains majority weight. Null, 0, or negative values disable smoothing entirely.

Blend formula (for a slot with `n` neighbours present, where `n` is 1 or 2):

```
blended(s) = (1 − n × w) × agg(s)  +  w × sum(agg(neighbours))
cell(s)    = max(agg(s), blended(s))
```

A value around **0.10–0.15** is a safe starting point. Use this when diagnostic output shows single-slot "holes" — one low slot surrounded by high-traffic neighbours that causes unnecessary scale-down/scale-up churn.

```yaml
ForecastBaselineSmoothingNeighbourWeight: 0.12
```

---

## Step 5 — Boost

### `ForecastBaselineBoostFactor`

Multiplies every baseline cell by a fixed factor **after** smoothing and **before** any `ForecastMode` snap or anchor transform. Use it to add a simple, uniform safety margin.

- `1.10` → all cells are 10% higher than the smoothed baseline.
- Null, NaN, infinite, or ≤ 0 values are all treated as `1.0` (no change).

```yaml
ForecastBaselineBoostFactor: 1.15
```

**Boost vs `ForecastSnapPercentile`**

Both add headroom, but in different ways:

- `ForecastBaselineBoostFactor` scales every cell **uniformly** by a percentage, regardless of variance. It is simple and predictable.
- `ForecastSnapPercentile` summarises the spread of values **within each snap window**. It adds more margin where load is variable and less where it is stable.

Use boost for a flat global allowance. Use a higher snap percentile when you want extra margin to track how variable the load is within each window.

---

## Tuning guide

Start from the simplest configuration and add complexity only when you observe a concrete need in the diagnostic output.

1. **Start with `Max` and nothing else.** Establish that the forecast is safe and conservative.

2. **Enable capped-point correction** (`ForecastMetricMax` + defaults) if you see the baseline systematically under-forecasting at times when the resource was at capacity.

3. **Switch to `WeightedMean` with `ForecastBaselineDecayDays: 21`** if recent load trends should carry more weight than older spikes. The default τ of 14 days is already recency-biased; 21 days is a good middle ground.

4. **Add smoothing around `0.12`** if the baseline shows isolated low slots between high-traffic periods that cause unnecessary scale-down/scale-up cycles.

5. **Add a small boost (`1.05–1.15`)** if you want extra capacity margin without touching the snap or anchor configuration.

6. **Check diagnostic lines** after each change — they report the effective aggregation mode, smoothing weight (when active), and boost factor applied.

---

See also: XML `<remarks>` on each property in `Metric.cs` for quick cross-references while browsing the code.
