## Why

When a wildcard ResourceId (e.g. `databases/*`) expands to multiple databases, resources of different SKU models (DTU standalone, elastic pool, vCore) all enter the same processing loop. Metrics and dimensions are model-specific — fetching `dtu_consumption_percent` against a vCore database returns a 400 error and disables the resource for one hour. There is no config-level mechanism to include or exclude resources by their runtime properties; operators must either list databases explicitly or rely on silent internal filtering that isn't visible in the YAML.

## What Changes

- A new optional `ResourceFilter` string property on `ResourceInstance` accepts a C# lambda expression evaluated at resource discovery time.
- A new lean `ResourceFilterContext` class exposes `ResourceName` (string), `Tags` (Azure resource tags), and `Resource` (the raw ARM object). Operators access resource-type-specific properties directly via `r.Resource` — the same pattern already used in `DataExpression` lambdas (`data.Resource.Data.Sku.Name`).
- The filter is compiled via the existing `ExpressionParserUtils` infrastructure and applied inside each wildcard expansion helper, so resources that do not match are never added to the processing loop.
- `MyCustomTypeProvider` is updated to expose `ResourceFilterContext` to Dynamic LINQ.
- `IResourceStateFactory.ExpandResourcesAsync` and all expansion helpers are updated to accept and apply the compiled filter.

## Capabilities

### New Capabilities

- `resource-instance-filter`: Allows operators to write a lambda expression in `ResourceInstance.ResourceFilter` that is evaluated for each expanded resource during discovery. Resources returning `false` are excluded from the processing loop entirely.

### Modified Capabilities

_(none — no existing spec-level behavior changes)_

## Impact

- **Configuration**: `ResourceInstance` gains a new optional `ResourceFilter` field.
- **Code**: `ResourceInstance.cs`, `Configuration.cs`, `IResourceStateFactory.cs`, `ResourceStateFactory.cs`, `ResourceManager.cs`, `MsSqlDatabaseResourceStateHelper.cs`, `MssqlElasticPoolResourceStateHelper.cs`, `AksNodePoolResourceStateHelper.cs` (and other expand helpers), `MyCustomTypeProvider.cs`.
- **New file**: `ResourceFilterContext.cs` (in `configuration/` or `resourcemanagement/Dto/`).
- **No breaking changes** — `ResourceFilter` is optional; existing configs without it behave identically.
