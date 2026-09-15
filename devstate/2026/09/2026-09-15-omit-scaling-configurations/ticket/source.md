Two related bugs in the same evaluation loop. Parent already diagnosed from source; verify on dest then fix.

### Bug 1 — NullReferenceException when ScalingConfigurations is omitted
YAML that has CustomMetrics (including Query rows) and omits the ScalingConfigurations key binds Resource.ScalingConfigurations as **null**. Configuration.PrepareAndValidate already treats that as valid (if (resource.ScalingConfigurations == null) continue;). ResourceProcessor.RunLoop does state.Configuration.ScalingConfigurations.Values and NREs. Existing test ProcessOneAsync_WhenNoScalingConfigurations_StillRefreshesAndExitsBeforeScaling only covers an **empty dictionary**, not null — so CI did not catch YAML-omit.

Intended behavior already documented by that test: Refresh + CustomMetrics push still run; then exit before scaling when there is nothing to evaluate.

Required: treat null like empty. Log **Information** that this resource has no ScalingConfigurations configured (name the resource from ResourceState identity the logger already uses). Do not use Trace for this line (time-window "none apply right now" stays Trace). Throttle the new INFO like the existing disabled-resource INFO (once per hour) so a 1-minute Frequency does not flood.

### Bug 2 — "disabled for 1 hour" does not skip the next cycles
ProcessOneAsync catch sets DisabledUntil[UnhandledExceptionPrefix + ex.Message] = UtcNow + UnhandledExceptionDisableDuration and logs the 1-hour warning. finally always ResetEvaluation() so NextEvaluationSeconds() only waits Frequency, not one hour. RunLoop calls IsDisabled() **after** the ScalingConfigurations.Values access (and after Refresh/push). The NRE therefore throws again every Frequency (e.g. every minute): catch re-sets the same key, warn repeats, disable is never read.

Fix the cause (skill:opd-commandments:Fix the cause, Linear coding): honor IsDisabled() in ProcessOneAsync **before** RunLoop, with the same once-per-hour disabled INFO already in RunLoop (do not duplicate spam). Keep the in-RunLoop IsDisabled() after Refresh so a disable discovered during Refresh (e.g. external tag) still skips scaling. Do not paper over by catching NRE only.

### Tests
- Null ScalingConfigurations (YAML-omit shape): no throw; Refresh still runs; INFO path covered or asserted via logger if the suite already captures logs.
- After an unhandled exception disable, a later ProcessOneAsync within the hour does not re-enter the failing work / does not log another fail+1-hour warn.
Reuse existing ResourceProcessorTests helpers. No production identifiers.
