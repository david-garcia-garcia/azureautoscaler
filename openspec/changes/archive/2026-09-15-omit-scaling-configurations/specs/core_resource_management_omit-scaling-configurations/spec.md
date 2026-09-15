## Purpose

Lets operators run custom-metrics refresh and push on a resource without defining scaling rules, and keeps evaluation stable when scaling configuration is absent or when a resource is temporarily disabled after failures.

## ADDED Requirements

### Requirement: Omitted scaling configurations are valid at bind time
The product SHALL accept a resource configuration where the `ScalingConfigurations` key is omitted from YAML and deserializes as null. Startup validation SHALL NOT require a non-null scaling dictionary.

#### Scenario: Null scaling dictionary passes validation
- **WHEN** configuration is prepared for a resource with `ScalingConfigurations` absent from YAML
- **THEN** validation completes without error for the missing scaling dictionary

### Requirement: Evaluation refreshes and pushes without scaling when no configurations exist
When a resource has no scaling configurations (null or an empty dictionary), each due evaluation cycle SHALL run refresh and custom-metrics push when configured, SHALL NOT run scaling evaluation, and SHALL NOT throw because scaling configuration is missing.

#### Scenario: Null dictionary completes refresh and push
- **WHEN** a due evaluation runs for a resource whose scaling dictionary is null
- **THEN** refresh runs
- **AND** custom-metrics push runs when due
- **AND** no scaling evaluation runs
- **AND** the cycle completes without unhandled exception

#### Scenario: Empty dictionary completes refresh and push
- **WHEN** a due evaluation runs for a resource whose scaling dictionary is empty
- **THEN** refresh runs
- **AND** custom-metrics push runs when due
- **AND** no scaling evaluation runs

### Requirement: Distinct logging for absent versus inactive scaling configurations
When no scaling configurations exist (null or empty dictionary), the product SHALL emit Information at most once per hour per resource identifying that scaling is not configured. When a non-empty dictionary exists but no entry is active for the current time window, the product SHALL emit Trace indicating no configuration applies now, without replacing the hourly Information for the absent case.

#### Scenario: Hourly Information for null or empty dictionary
- **WHEN** a due evaluation completes refresh and push for a resource with null or empty scaling dictionary
- **THEN** Information is emitted at most once per hour per resource that scaling configurations are not configured

#### Scenario: Trace when dictionary exists but none active
- **WHEN** a due evaluation runs for a resource with a non-empty scaling dictionary
- **AND** no entry is active for the current time window
- **THEN** Trace indicates no scaling configuration applies right now
- **AND** the hourly “not configured” Information is not used for this case

### Requirement: Disable is honored before evaluation work
When a resource is disabled (including after an unhandled evaluation exception with a one-hour disable window), the product SHALL skip refresh, push, and scaling for that cycle if disable is still active at the start of `ProcessOneAsync`. Disable discovered during refresh MAY still be honored before scaling evaluation on cycles that entered evaluation while not disabled.

#### Scenario: Second cycle within disable window skips work
- **WHEN** a resource was disabled for one hour due to an unhandled evaluation exception
- **AND** a subsequent due evaluation starts before the disable expires
- **THEN** refresh, push, and scaling do not run for that cycle
- **AND** the evaluation does not repeat the failing path

#### Scenario: Disabled Information is throttled once per hour
- **WHEN** evaluation is skipped or exits early because the resource is disabled
- **THEN** disabled Information is emitted at most once per hour per resource
- **AND** early skip and post-refresh disable checks share the same throttle behavior
