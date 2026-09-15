## Context

See `proposal.md` — Why. `Configuration.PrepareAndValidate` already allows null `ScalingConfigurations`. `ResourceProcessor.RunLoop` today builds the active scaling list from `ScalingConfigurations.Values` before refresh, so null throws and prevents refresh, push, and disable checks from running in the intended order. `ProcessOneAsync` has no upfront disable guard.

## Goals / Non-Goals

**Goals:**
- Align null and empty scaling dictionaries with the post-refresh exit path used when no configuration is active.
- Run refresh and push before any scaling-configuration enumeration that assumes a non-null dictionary.
- Honor disable before entering `RunLoop`; share one throttled disabled Information path between early return and post-refresh check.
- Add `ResourceState` throttle for no-config Information (sibling to `LastDisabledMessageLogged`).
- Cover null YAML shape and disable-on-second-cycle in `ResourceProcessorTests`.

**Non-Goals:**
- Requiring `ScalingConfigurations` in YAML or changing validation to reject null.
- Catching null-reference only without fixing disable ordering.
- Changing transient Azure short-disable semantics (`transient-azure-error-short-disable` spec).

## Decisions

### Decision 1 — Defer `.Values` until after refresh and push

**Choice:** Move scaling-configuration enumeration to after `Refresh` and `PushIfDueAsync`; treat null like empty when deciding whether scaling runs.

**Rationale:** Matches explore decision and existing empty-dict behavior; fixes pre-refresh NRE.

**Alternative:** Null-coalesce to empty dictionary at bind time. Rejected — out of scope for validation changes and hides YAML-omit shape in tests.

### Decision 2 — Shared throttled disabled log helper

**Choice:** Private helper on the processor invoked from `ProcessOneAsync` when disabled before `RunLoop`, and from the existing post-refresh disable check in `RunLoop`.

**Rationale:** One owner for `LastDisabledMessageLogged` throttling; satisfies Desired without duplicate spam.

### Decision 3 — `LastNoScalingConfigurationsMessageLogged` on state

**Choice:** New timestamp field on `ResourceState`, parallel to `LastDisabledMessageLogged`.

**Rationale:** Explore confirmed no existing field covers no-config Information.

### Decision 4 — Information vs Trace split

**Choice:** Null or `{}` → hourly Information; non-empty dict with zero active windows → Trace only (unchanged wording intent).

**Rationale:** Operators omitting scaling vs time-window mismatch are different signals.

## Risks / Trade-offs

- **Risk:** Early disable skip skips refresh while disabled — **Mitigation:** Intended; disable after unhandled failure should rest the resource; post-refresh disable still applies when refresh sets disable during an active cycle.
- **Risk:** Tests without mock logger cannot assert Information — **Mitigation:** Use existing `Mock<ILogger>` patterns; skip assert only where logger is not mockable.
