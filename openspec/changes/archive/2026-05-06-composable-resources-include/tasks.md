## 1. Dependencies

- [x] 1.1 Add `YamlDotNet` as an explicit `<PackageReference>` in `poolautoscaler.csproj` (it is currently only a transitive dep via `NetEscapades.Configuration.Yaml`)

## 2. YAML Pre-processor

- [x] 2.1 In `Program.cs`, implement a static helper method `LoadConfigWithIncludes(string configFilePath)` that: reads the main YAML file, walks the `Resources` sequence, detects `{ $include: <path> }` nodes, resolves each path relative to the config file directory, reads and parses the referenced file as a YAML sequence, splices the loaded nodes in place, and returns the merged YAML as a `string` (or `MemoryStream`)
- [x] 2.2 Log each included file at `Debug` level as it is loaded (log the resolved absolute path)
- [x] 2.3 Throw a descriptive `FileNotFoundException` if an included file does not exist (include the resolved path in the message)
- [x] 2.4 Throw a descriptive `InvalidOperationException` if the root node of an included file is not a YAML sequence

## 3. Wire Pre-processor into Config Loading

- [x] 3.1 In `CreateHostBuilder` → `ConfigureAppConfiguration`, replace the `config.AddYamlFile(file, ...)` call with: call `LoadConfigWithIncludes(file)`, then use `config.AddYamlStream(new MemoryStream(Encoding.UTF8.GetBytes(merged)))` (disable `reloadOnChange` since we lose the file watcher)

## 4. Documentation

- [x] 4.1 Add a section to `docs/configuration.md` explaining the `$include` syntax, showing an example `config.yml` and an example included file, and noting the limitations (no nested includes, no reload watching of included files)
