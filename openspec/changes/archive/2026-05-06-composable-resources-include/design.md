## Context

The autoscaler uses `NetEscapades.Configuration.Yaml` to load `config.yml` via .NET's `IConfiguration` pipeline (`AddYamlFile`). The YAML is parsed into a flat key-value store and then bound to a typed `Configuration` POCO via `configuration.Get<Configuration>()`. The `Resources` property is a `List<Resource>`.

The .NET `IConfiguration` system has no concept of file inclusion. The `NetEscapades` library processes YAML as-is. Therefore, `$include` support must be implemented as a pre-processing step before the YAML reaches `IConfiguration`.

`YamlDotNet` is already available as a transitive dependency of `NetEscapades.Configuration.Yaml`, so no new packages are needed.

## Goals / Non-Goals

**Goals:**
- Allow `$include: <relative-path>` items anywhere in the `Resources` sequence.
- Expand include entries by reading the referenced file and splicing its resource entries in-place.
- Paths are resolved relative to the directory of the main `config.yml`.
- Inline resource entries and `$include` entries can be freely mixed.
- No changes to any configuration model classes (`Configuration.cs`, `Resource.cs`, etc.).

**Non-Goals:**
- Nested includes (an included file cannot itself use `$include`).
- `reloadOnChange` watching of included files.
- Include support for any section other than `Resources`.
- Glob patterns or directory-level includes.

## Decisions

### D1: Pre-process YAML before IConfiguration, not after binding

**Decision:** Read the main YAML file, expand `$include` entries in the `Resources` sequence using `YamlDotNet`, serialize back to a `MemoryStream`, and call `AddYamlStream()` instead of `AddYamlFile()`.

**Alternatives considered:**
- *Post-bind in `PrepareAndValidate`*: Would require `Resource.cs` to carry a structural `$include` marker property, mixing config-model concerns with loader concerns. Also requires a separate YAML-loading step mid-application boot. Rejected.
- *Custom `IConfigurationSource`/`IConfigurationProvider`*: Cleaner architecture but ~3x more boilerplate for a single-file-single-feature use case. Rejected for now; could be refactored to this if more source types are needed later.

### D2: `$include` as a sequence item (not a sibling key)

**Decision:** The directive is expressed as an item in the `Resources` list:
```yaml
Resources:
  - $include: resources/aks.yaml
  - $include: resources/sql.yaml
  - Resources:          # inline entry still works
      my_resource:
        ResourceId: "..."
    Frequency: 5m
```

**Alternatives considered:**
- *`$include` as a sibling map key on `Resources`*: e.g., `Resources: { $include: [...] }`. Forces `Resources` to be a map rather than a sequence, breaking the existing list binding entirely. Rejected.

### D3: Included file format = bare YAML sequence of resource entries

**Decision:** An included file contains a YAML sequence at the root — the same structure as the items you'd put inline under `Resources`. No wrapper key required.

```yaml
# resources/aks.yaml
- Resources:
    aks_dev:
      ResourceId: "..."
  Frequency: 5m
  Enabled: true
```

This keeps included files minimal and readable on their own.

## Risks / Trade-offs

- **`reloadOnChange` does not watch included files** → Changes to included files require a container/process restart (or touching `config.yml` to trigger reload). Accepted trade-off given the DX use case.
- **Error messages reference the merged stream, not the original file** → If a YAML parse error exists in an included file, the error line number will be relative to the merged document. Mitigated by logging which files are being included at startup so the user can identify the source.
- **`AddYamlStream` vs `AddYamlFile`** → Switching to stream-based loading means we lose the built-in file-watcher. Acceptable since the trade-off is already accepted above.
- **`YamlDotNet` version drift** → Since it is a transitive dep (not a direct one), a future `NetEscapades` upgrade could change the available `YamlDotNet` version. Mitigated by adding it as an explicit package reference.
