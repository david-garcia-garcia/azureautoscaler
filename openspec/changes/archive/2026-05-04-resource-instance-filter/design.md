## Context

Resource discovery uses wildcard expansion to enumerate all matching Azure resources (e.g. `databases/*` lists every database under a server). All expanded resources enter the processing loop regardless of their properties. Metrics and dimensions are model-specific, so a DTU-targeted config block applied to a vCore or elastic pool database produces 400 errors from Azure Monitor, triggering a 1-hour resource lockout.

The codebase already has `ExpressionParserUtils` (Dynamic LINQ) for compiling C# lambda strings at startup, `IsDtuModel` in `MsSqlDatabaseResourceStateHelper`, and `CanApplyDimension` filtering at rule-execution time. What is missing is an operator-visible, config-level mechanism to exclude resources before they enter the loop.

## Goals / Non-Goals

**Goals:**
- Allow operators to write a predicate lambda in `ResourceInstance.ResourceFilter` that is evaluated once per expanded resource during discovery.
- Resources returning `false` are excluded from the discovered set and never processed.
- Compile the predicate at startup (fail-fast on bad expressions).
- Provide a `ResourceFilterContext` with pre-computed, human-friendly properties so operators don't need to know internal ARM types or cast.
- Apply filtering inside each wildcard expansion helper (data already fetched there, no extra API calls).

**Non-Goals:**
- Runtime re-evaluation of the filter after discovery (filter is expansion-time only).
- Lambda access to metric data or scaling state (use ScaleUpCondition for that).
- Filtering non-wildcard (literal) resource IDs — the filter is silently ignored for non-expanded resources.
- Changing any behavior for configs that omit `ResourceFilter`.

## Decisions

### Decision 1: Expansion helpers return context; filtering is centralized in ResourceManager

**Chosen**: `ExpandResourcesAsync` (and each expansion helper) returns `Dictionary<string, ExpandedResource>` instead of `Dictionary<string, string>`. `ExpandedResource` carries both the `ResourceId` string and a `ResourceFilterContext` built from the ARM data already fetched during expansion. `ResourceManager.DiscoverCoreAsync` evaluates the compiled filter against the context after expansion, before calling `Create()`, and logs each exclusion at `Information` level.

**Alternative considered**: Pass the compiled filter *into* each expansion helper and apply it inside the `foreach` loop. This works but scatters filter evaluation across every helper, prevents centralized logging (the helper can't easily report "X of Y excluded"), and requires threading the filter through `IResourceStateFactory.ExpandResourcesAsync` and every call site.

**Rationale**: Expansion helpers already fetch the ARM resource data in their loop — returning a `ResourceFilterContext` alongside the ID costs nothing extra. Centralizing the filter decision in `ResourceManager` enables a single, accurate log line ("N discovered, M filtered out, K added"), keeps expansion helpers filter-unaware, and avoids changing `IResourceStateFactory`'s parameter surface. For non-wildcard (literal) resource IDs, the expansion helper sets `Context = null`; `ResourceManager` skips the filter evaluation and always includes the resource.

### Decision 2: `ResourceFilterContext` as the lambda parameter type — generic with a `Properties` bag

**Chosen**: Compile the filter as `Func<ResourceFilterContext, bool>`. `ResourceFilterContext` contains:
- `ResourceName` (`string`) — always populated
- `Tags` (`IDictionary<string, string>`) — Azure resource tags (available on all ARM resources)
- `Resource` (`object`) — the raw ARM resource; Dynamic LINQ resolves members against the actual runtime type

Operators access resource-specific data directly via `r.Resource` (e.g. `r.Resource.Data.Sku.Family == null`), which is identical to the existing `DataExpression` pattern where `data.Resource.Data.Sku.Name` is already used in production configs. No `Properties` bag is needed.

**Alternative A**: Bake resource-type-specific flags (`IsDtuModel`, `IsElasticPool`) as first-class properties on the context class, or a `Properties` dictionary — either adds pre-computation work to every expansion helper and duplicates data already accessible on the ARM object.

**Alternative B**: `Func<ArmResource, bool>` using the base class directly — would require ARM SDK casts for concrete type members.

**Alternative C**: `Func<dynamic, bool>` — no compile-time validation; errors surface at runtime.

**Rationale**: `CustomMetricDataContext.Resource` is already `object`-typed and operators already write `data.Resource.Data.Sku.Name` in real configs. Dynamic LINQ resolves the members against the runtime type. Reusing this exact pattern keeps the context class minimal and eliminates all pre-computation from expansion helpers — they simply set `Resource = <arm object>`.

### Decision 3: Compile at startup in `Configuration.PrepareAndValidate`

**Chosen**: Compile `ResourceFilter` to `ResourceFilterExpression` during `PrepareAndValidate`, same as all other lambda expressions.

**Rationale**: Fail-fast on bad expressions before any Azure calls are made. Consistent with existing pattern for `ScaleUpCondition`, `ScaleDownCondition`, `DataExpression`.

### Decision 4: `ResourceFilterContext` placed in `configuration/` namespace

**Chosen**: `poolautoscaler.configuration.ResourceFilterContext`.

**Rationale**: It is a configuration-layer concept (the contract between YAML and the filter lambda). Keeping it adjacent to `ResourceInstance` and `Metric` makes the ownership clear. It does not depend on `resourcemanagement` internals.

### Decision 5: `ExpandResourcesAsync` return type changes to carry context

**Chosen**: `IResourceStateFactory.ExpandResourcesAsync` returns `Dictionary<string, ExpandedResource>` (a new thin type: `ResourceId` string + `ResourceFilterContext?`). All existing callers that only needed the ID string are updated. Expansion helpers build and return a `ResourceFilterContext` for each wildcard-expanded resource; for non-wildcard (literal) resources the context is `null`.

**Alternative**: Keep the return type as `Dictionary<string, string>` and add a parallel context dictionary. This avoids touching the return type but makes the API awkward and forces callers to correlate two dictionaries by key.

**Rationale**: The return type change is contained to an internal interface. It makes the contract explicit: expansion now produces both an ID and the data needed for filtering. `ResourceManager` is the only consumer of `ExpandResourcesAsync` so the blast radius of the type change is small.

## Risks / Trade-offs

- **Risk**: Operators write filters that exclude all resources silently → nothing gets scaled. Mitigation: log `LogInformation` for each resource excluded by the filter during expansion, including the resource name and the filter expression source.
- **Risk**: `ResourceFilterContext` properties populated from ARM types change with SDK upgrades. Mitigation: map only stable, documented properties (`Sku.Name`, `Sku.Family`, `ElasticPoolId`).
- **Risk**: Filter expression references types not known to Dynamic LINQ. Mitigation: `ResourceFilterContext` is added to `MyCustomTypeProvider`; simple boolean/string properties don't require additional type registrations.
- **Trade-off**: Expansion helpers for resource types that don't support wildcards (Fabric Capacity, Azure DevOps) receive the filter but it has no effect. Acceptable — non-wildcard resources produce a single-entry dictionary and the filter has no ARM object to evaluate against. Document this in the property summary.

## Migration Plan

No migration required. `ResourceFilter` is optional; existing configs without it compile and run identically. The new optional parameter on `ExpandResourcesAsync` defaults to `null`, preserving all existing call sites.
