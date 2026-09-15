# Test coverage

Ticket job (source: `devstate/requirement.md` Desired): null `ScalingConfigurations` must refresh and custom-metrics push without NRE, skip scaling safely, emit throttled absent-config Information, and honor `IsDisabled()` before `RunLoop`.

1. [hard] Edge case untested — `autoscaler/resourcemanagement/ResourceProcessor.cs:286-318` — `HasNoScalingConfigurations` treats empty dictionary like null and calls `LogNoScalingConfigurationsInformationIfDue`; test `(none)` for empty-only Information — `ProcessOneAsync_WhenNoScalingConfigurations_StillRefreshesAndExitsBeforeScaling` only asserts `RefreshWasCalled` (`autoscalertests/ResourceProcessorTests.cs:567-587`)
   → After refresh/push with `ScalingConfigurations = {}`, assert Information contains “no scaling configurations configured” (mock logger), same as the null test
   Status: done
   Argument: Empty-dict test now verifies the Information line on a mock logger.
2. [hard] Edge case untested — `autoscaler/resourcemanagement/ResourceProcessor.cs:277-283` — `LogNoScalingConfigurationsInformationIfDue` throttles via `LastNoScalingConfigurationsMessageLogged` and `TotalHours >= 1`; test `(none)` for a second due `ProcessOneAsync` within the same hour — `ProcessOneAsync_WhenScalingConfigurationsNull_StillRefreshesAndLogsNoConfigurationsInformation` calls once and expects `Times.Once` only (`autoscalertests/ResourceProcessorTests.cs:590-617`)
   → Run two due evaluations within one hour with null config; assert Information for absent scaling is logged once, not twice
   Status: done
   Argument: Null test runs two due cycles (`FrequencyParsed` zero) and still expects Information once.
