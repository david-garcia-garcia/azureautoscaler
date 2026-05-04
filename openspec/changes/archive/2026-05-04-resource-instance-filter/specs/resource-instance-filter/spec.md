## ADDED Requirements

### Requirement: ResourceInstance accepts a ResourceFilter lambda expression
`ResourceInstance` SHALL expose an optional `ResourceFilter` string property containing a C# lambda expression. When set, the system SHALL compile it at startup as `Func<ResourceFilterContext, bool>` and store it in `ResourceFilterExpression`. If the expression fails to compile, startup SHALL fail with a descriptive error.

#### Scenario: Valid filter compiles at startup
- **WHEN** `ResourceFilter` is set to a syntactically valid lambda (e.g. `"(r) => r.Resource.Data.Sku.Family == null"`)
- **THEN** `ResourceFilterExpression` is non-null and startup succeeds

#### Scenario: Invalid filter fails at startup
- **WHEN** `ResourceFilter` contains a lambda that does not compile (e.g. `"(r) => r.Nonexistent"`)
- **THEN** startup throws an exception with a message indicating the invalid expression

#### Scenario: Absent filter is ignored
- **WHEN** `ResourceFilter` is null or not set in the configuration
- **THEN** `ResourceFilterExpression` is null and all expanded resources are included as before

---

### Requirement: ResourceFilterContext exposes resource properties for filtering
The system SHALL provide a `ResourceFilterContext` class as the lambda parameter. It SHALL expose:
- `ResourceName` (`string`) — the name of the resource (database name, pool name, share name, etc.), always populated
- `Tags` (`IDictionary<string, string>`) — the Azure resource tags at expansion time, always populated (empty dict if none)
- `Resource` (`object`) — the raw ARM resource object (e.g. `SqlDatabaseResource`); Dynamic LINQ resolves members against the actual runtime type, consistent with the existing `CustomMetricDataContext.Resource` pattern

Operators access resource-type-specific properties directly via `r.Resource` (e.g. `r.Resource.Data.Sku.Family`), the same way `DataExpression` already uses `data.Resource.Data.Sku.Name`.

#### Scenario: SQL DTU standalone database accessible via Resource
- **WHEN** a wildcard expands to a SQL database with SKU Name `"Standard"` and no Family
- **THEN** `r.Resource.Data.Sku.Name` evaluates to `"Standard"` and `r.Resource.Data.Sku.Family` evaluates to `null` in the filter expression

#### Scenario: Filter expression accesses ARM properties at runtime
- **WHEN** `ResourceFilter` is `"(r) => r.Resource.Data.Sku.Family == null && r.Resource.Data.Sku.Name != \"ElasticPool\""`
- **THEN** the expression correctly identifies standalone DTU databases and excludes elastic pool and vCore databases

#### Scenario: Tags are accessible for tag-based filtering
- **WHEN** `ResourceFilter` is `"(r) => r.Tags.ContainsKey(\"env\") && r.Tags[\"env\"] == \"prod\""`
- **THEN** only resources tagged `env=prod` are included in the discovered set

---

### Requirement: Resources failing the filter are excluded from discovery with summary logging
When `ResourceFilterExpression` is set, the system SHALL evaluate it inside `ResourceManager.DiscoverCoreAsync` after expansion but before creating the resource state, using the `ResourceFilterContext` returned alongside each resource ID. Resources for which the expression returns `false` SHALL NOT be added to the discovered resource set. After processing each `ResourceInstance`, the system SHALL emit a single `Information`-level summary log reporting the counts of discovered, filtered-out, and added/kept resources.

#### Scenario: DTU-only filter excludes vCore databases
- **WHEN** `ResourceFilter` is `"(r) => r.Resource.Data.Sku.Family == null && r.Resource.Data.Sku.Name != \"ElasticPool\""` and the wildcard expands to a mix of DTU and vCore databases
- **THEN** only DTU databases are added to the resource set; vCore databases are excluded

#### Scenario: Summary log is emitted per ResourceInstance
- **WHEN** a wildcard expands to 5 databases and 3 pass the filter
- **THEN** an `Information`-level log is emitted stating 5 were discovered, 2 were filtered out, and 3 were added or kept

#### Scenario: Included resources behave identically to unfiltered resources
- **WHEN** a resource passes the filter (returns `true`)
- **THEN** it is added to the discovered set and processed exactly as it would be without a filter

#### Scenario: Filter with no wildcard has no effect
- **WHEN** `ResourceFilter` is set on a `ResourceInstance` whose `ResourceId` does not contain a wildcard pattern
- **THEN** the single resource is included regardless of the filter expression (`Context` is null for literal IDs; the filter is skipped)

---

### Requirement: ResourceFilterContext is available to Dynamic LINQ expressions
The system SHALL register `ResourceFilterContext` in `MyCustomTypeProvider` so that Dynamic LINQ can resolve it without requiring fully-qualified type names in the lambda string.

#### Scenario: Filter lambda references ResourceFilterContext properties without qualification
- **WHEN** `ResourceFilter` is `"(r) => r.Resource.Data.Sku.Family == null"` (no namespace prefix on `ResourceFilterContext`)
- **THEN** the expression compiles successfully
