# Standards

1. [hard] Leave a trail — `autoscaler/resourcemanagement/ResourceProcessor.cs:286` — new helper `HasNoScalingConfigurations` has no succinct job comment (`private static bool HasNoScalingConfigurations(Resource configuration) { return configuration.ScalingConfigurations == null || configuration.ScalingConfigurations.Count == 0; }`)
   → Add a `<summary>` describing null or empty scaling dictionary
   Status: done
   Argument: Added job `<summary>` on `HasNoScalingConfigurations`.

2. [hard] Leave a trail — `autoscaler/resourcemanagement/ResourceProcessor.cs:291` — new helper `GetActiveScalingConfigurations` has no succinct job comment despite a multi-step body (empty guard, `ConfigFinder`, LINQ filter)
   → Add a `<summary>` describing active scaling entries for the evaluation instant
   Status: done
   Argument: Added job `<summary>` on `GetActiveScalingConfigurations`.

3. [judgement] Duplicated Code — `autoscaler/resourcemanagement/ResourceProcessor.cs:288` and `:294` — the same null-or-empty scaling dictionary guard appears in `HasNoScalingConfigurations` and again at the start of `GetActiveScalingConfigurations`
   → Have `GetActiveScalingConfigurations` call `HasNoScalingConfigurations(state.Configuration)` before building the active list
   Status: skipped
   Argument: judgement; defensive empty return is unused after the RunLoop early exit.
