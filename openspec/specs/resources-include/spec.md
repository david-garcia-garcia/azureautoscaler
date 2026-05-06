# resources-include Specification

## Purpose
TBD - created by archiving change composable-resources-include. Update Purpose after archive.
## Requirements
### Requirement: Resources array supports $include directive
The system SHALL allow any item in the top-level `Resources` sequence of `config.yml` to be a `$include` directive that references an external YAML file. The referenced file SHALL be loaded and its contents spliced into the `Resources` list in place of the `$include` entry before the configuration is bound.

#### Scenario: Single include file is expanded
- **WHEN** `config.yml` contains `- $include: resources/aks.yaml` in the `Resources` array
- **THEN** the system SHALL load `resources/aks.yaml` (relative to `config.yml`) and replace the `$include` entry with all resource entries from that file

#### Scenario: Multiple include files are expanded in order
- **WHEN** `config.yml` contains multiple `$include` entries in the `Resources` array
- **THEN** the system SHALL expand each `$include` in the order they appear, preserving the overall sequence order

#### Scenario: Inline entries and $include entries can be mixed
- **WHEN** the `Resources` array contains both inline resource entries and `$include` entries
- **THEN** the system SHALL expand includes and retain inline entries, producing a single merged list in declaration order

#### Scenario: Include path is resolved relative to main config file
- **WHEN** a `$include` path is specified as `resources/aks.yaml`
- **THEN** the system SHALL resolve it relative to the directory containing `config.yml`, not the working directory of the process

#### Scenario: Missing include file produces a clear error
- **WHEN** a `$include` references a file that does not exist
- **THEN** the system SHALL throw an exception with a message that includes the missing file path and SHALL NOT start the service

#### Scenario: Included file must contain a YAML sequence
- **WHEN** a `$include` references a file whose root YAML node is not a sequence
- **THEN** the system SHALL throw an exception describing the expected format and SHALL NOT start the service

### Requirement: Nested includes are not supported
The system SHALL NOT process `$include` directives found inside an included file. Only the top-level `config.yml` `Resources` array is processed for includes.

#### Scenario: $include inside an included file is ignored
- **WHEN** an included resource file itself contains a `$include` entry
- **THEN** the system SHALL treat that entry as a literal resource object (which will fail YAML binding), not as a recursive include

### Requirement: Startup logs which files are loaded
The system SHALL log each included file path at `Debug` level during configuration loading so operators can trace the composed resource list.

#### Scenario: Include files are logged at startup
- **WHEN** one or more `$include` entries are present and successfully expanded
- **THEN** the system SHALL emit a debug log message for each file indicating it was loaded as part of the resource configuration

