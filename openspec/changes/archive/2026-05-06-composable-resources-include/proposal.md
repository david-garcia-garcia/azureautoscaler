## Why

`config.yml` is growing large and hard to manage as more resources are added. Users need a way to split the `Resources` array across multiple files to keep the configuration organized — particularly useful when managing different resource types or environments in separate files.

## What Changes

- The `Resources` array in `config.yml` supports a new `$include` directive that references an external YAML file containing a list of resource entries.
- The included file is resolved relative to the main `config.yml` directory.
- Inline resource entries and `$include` entries can be freely mixed in the same `Resources` list.
- The pre-processing happens before the YAML is handed to the .NET `IConfiguration` pipeline — all existing parsing, validation, and binding is unchanged.

## Capabilities

### New Capabilities

- `resources-include`: Support `$include` directive in the `Resources` array to compose the resource list from multiple YAML files.

### Modified Capabilities

_(none)_

## Impact

- `autoscaler/Program.cs`: New YAML pre-processing step replaces `AddYamlFile()` with a custom loader that expands `$include` entries before handing the merged YAML to `IConfiguration`.
- No changes to `Configuration.cs`, `Resource.cs`, or any other configuration model.
- No new NuGet packages required (`YamlDotNet` is already a transitive dependency).
- `reloadOnChange` will not watch included files — only the main `config.yml` triggers a reload (acceptable trade-off for this use case).
- Documentation (`docs/configuration.md`) needs a new section describing the `$include` syntax.
