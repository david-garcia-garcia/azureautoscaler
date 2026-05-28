## MODIFIED Requirements

### Requirement: Transient Azure errors use a short disable window
When a scale operation fails with a known transient Azure API error code, the resource SHALL be disabled for a short built-in duration rather than the generic unhandled-exception disable duration (1 hour). This applies to failures caught in the background scale task. The error-to-duration map is a hardcoded implementation detail of each resource type and is not user-configurable via YAML.

Built-in codes for SQL Elastic Pool: `ElasticPoolUpdateLinksNotInCatchup → 10 minutes`, `ElasticPoolBusy → 10 minutes`, `ElasticPoolDecreaseStorageLimitBelowUsage (on storage scale-up) → 5 minutes`.

#### Scenario: Known transient error disables for short duration
- **WHEN** a scale operation fails with an `Azure.RequestFailedException` whose `ErrorCode` matches a built-in entry in the resource's transient error map
- **THEN** the resource is disabled for the built-in number of minutes for that error code
- **AND** an informational log message is emitted indicating the error code, the short disable duration, and that the resource will resume automatically

#### Scenario: Unknown error falls through to generic handler
- **WHEN** a scale operation fails with an exception that does not match any built-in transient code
- **THEN** the existing generic unhandled-exception behavior applies (1-hour disable)

#### Scenario: Transient error details are surfaced via `TransientAzureOperationException`
- **WHEN** a resource's `ApplyChanges` implementation detects a known transient `RequestFailedException`
- **THEN** it SHALL rethrow as `TransientAzureOperationException` (carrying `ErrorCode` and `DisableMinutes`)
- **AND** the generic `ResourceProcessor` catches this exception and applies the short disable without requiring knowledge of Azure-SDK-specific types

#### Scenario: `ElasticPoolDecreaseStorageLimitBelowUsage` on storage scale-up is treated as transient
- **WHEN** a scale operation fails with `ElasticPoolDecreaseStorageLimitBelowUsage`
- **AND** the patch's `MaxSizeBytes` is strictly greater than `ExistingMssqlElasticPoolState.MaxSizeBytes` (storage scale-up)
- **THEN** the error is rethrown as `TransientAzureOperationException` with a 5-minute retry duration
- **AND** a CRITICAL-level log entry is emitted identifying the pool as stuck (MaxSizeBytes below actual data)

#### Scenario: `ElasticPoolDecreaseStorageLimitBelowUsage` on storage scale-down is not transient
- **WHEN** a scale operation fails with `ElasticPoolDecreaseStorageLimitBelowUsage`
- **AND** the patch's `MaxSizeBytes` is less than or equal to `ExistingMssqlElasticPoolState.MaxSizeBytes` (storage reduction or no storage change)
- **THEN** the error is NOT treated as transient — it propagates to the generic handler, resulting in a 1-hour disable
