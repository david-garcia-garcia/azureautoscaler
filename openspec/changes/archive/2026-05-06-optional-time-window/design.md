## Context

`ScalingConfiguration` has a `TimeWindow` property that is currently non-nullable and always required in YAML. The `Configuration.PrepareAndValidate` method unconditionally calls `TimeSpan.Parse` on `StartTime`/`EndTime` and `TimeZoneInfo.FindSystemTimeZoneById` on `TimeZone`, so a missing `TimeWindow` causes a `NullReferenceException` at startup.

`ConfigFinder` already treats `null`/whitespace for `Days` and `Months` as "match all" — the gap is `StartTime`/`EndTime` (no null guard) and `TimeWindow` itself (no null guard at the property level).

## Goals / Non-Goals

**Goals:**
- Users can omit `TimeWindow` entirely; the configuration is treated as always active.
- Users can supply a partial `TimeWindow` (e.g. only `Days: Weekday`); omitted fields default to all-day/all-period values.
- No breaking changes for existing configs that already supply a full `TimeWindow`.
- Test coverage for absent and partial `TimeWindow`.

**Non-Goals:**
- Changing the semantics of the `23:59` end-time (i.e. the last 60 seconds of midnight are not covered — same as existing behaviour).
- Supporting `TimeWindow` at the resource level (only per-`ScalingConfiguration`).

## Decisions

### Decision 1 — Defaults live in `TimeWindow` class, not in consuming code

**Choice:** Add C# property initialisers to `TimeWindow` for `Days = "All"`, `Months = "All"`, `StartTime = "00:00"`, `EndTime = "23:59"`.  
**Rationale:** Keeps defaults co-located with the model. Consuming code (`PrepareAndValidate`, `ConfigFinder`) needs no guards — they already handle "All" and will parse non-null strings. The YAML deserialiser (YamlDotNet) only calls property setters for keys that are present; absent keys leave the C# default intact.

**Alternative considered:** Materialise defaults in `PrepareAndValidate` (null-check `TimeWindow` and its fields before parsing). Rejected — scatters default logic across two files and requires null guards in `ConfigFinder` too.

### Decision 2 — Initialise `TimeWindow` to `new TimeWindow()` in `ScalingConfiguration`

**Choice:** `public TimeWindow TimeWindow { get; set; } = new TimeWindow();`  
**Rationale:** Guarantees `TimeWindow` is never `null` after deserialisation even when the YAML key is absent. Downstream code does not need null checks.

**Alternative considered:** Leave the property nullable and add null checks wherever `TimeWindow` is accessed. Rejected — more change surface and more maintenance burden.

## Risks / Trade-offs

- **Risk: YAML deserialiser resets property to null** — If the deserialiser explicitly sets absent object properties to `null`, the `= new TimeWindow()` default would be overwritten.  
  **Mitigation:** YamlDotNet does not call setters for absent keys; only present keys are set. This is the standard behaviour. Covered by integration-style tests.

- **Trade-off: "23:59" default leaves the last ~60 s of midnight out-of-window** — Consistent with existing configs (`Weekend: EndTime: "23:59"` in docs). Acceptable for a scheduling tool with ~4-minute polling frequency.

## Migration Plan

No migration required. Existing configs that already supply a full `TimeWindow` are unaffected — YAML values override C# defaults. The change is purely additive.
