## 1. New Types

- [x] 1.1 Create `configuration/ResourceFilterContext.cs` with `ResourceName` (string), `Tags` (IDictionary&lt;string,string&gt;), and `Resource` (object — Dynamic LINQ resolves members against the actual runtime type, same as `CustomMetricDataContext.Resource`)
- [x] 1.2 Create `resourcemanagement/Dto/ExpandedResource.cs` with `ResourceId` (string) and `Context` (ResourceFilterContext?, null for non-wildcard resources)
- [x] 1.3 Add `ResourceFilterContext` to `MyCustomTypeProvider.GetCustomTypes()`

## 2. Configuration Layer

- [x] 2.1 Add `ResourceFilter` (string) and `ResourceFilterExpression` (`Func<ResourceFilterContext, bool>?`) properties to `ResourceInstance`
- [x] 2.2 In `Configuration.PrepareAndValidate`, iterate `resource.Resources` and compile `ResourceFilter` via `ExpressionParserUtils.ParseExpression` into `ResourceFilterExpression` (fail-fast on parse error)

## 3. Expansion Interface & Return Type

- [x] 3.1 Change `IResourceStateFactory.ExpandResourcesAsync` return type from `Dictionary<string, string>` to `Dictionary<string, ExpandedResource>`
- [x] 3.2 Update `ResourceStateFactory.ExpandResourcesAsync` to match the new return type and thread `ExpandedResource` results from each expansion helper

## 4. SQL Database Expansion

- [x] 4.1 Change `MsSqlDatabaseResourceStateHelper.ExpandSqlDatabaseWildcard` return type to `Dictionary<string, ExpandedResource>`
- [x] 4.2 Inside the database `foreach`, build `ResourceFilterContext { ResourceName = database.Data.Name, Tags = database.Data.Tags, Resource = database }` and return it in `ExpandedResource` alongside the expanded resource ID

## 5. Other Expansion Helpers

- [x] 5.1 Change `MssqlElasticPoolResourceStateHelper.ExpandElasticPoolWildcard` to return `Dictionary<string, ExpandedResource>`, populating `ResourceFilterContext { ResourceName, Tags, Resource = elasticPool }`
- [x] 5.2 Change `AksNodePoolResourceStateHelper.ExpandNodePoolWildcard` to return `Dictionary<string, ExpandedResource>`, populating `ResourceFilterContext { ResourceName, Tags, Resource = agentPool }`
- [x] 5.3 Change `StorageFileShareResourceStateHelper.ExpandFileShareWildcard` to return `Dictionary<string, ExpandedResource>`, populating `ResourceFilterContext { ResourceName, Tags, Resource = share }`

## 6. ResourceManager — Centralized Filtering

- [x] 6.1 Update `ResourceManager.DiscoverCoreAsync` to iterate `Dictionary<string, ExpandedResource>` instead of `Dictionary<string, string>`
- [x] 6.2 After expanding each `ResourceInstance`, evaluate `resourceInstance.Value.ResourceFilterExpression` against each `ExpandedResource.Context` (skip evaluation when `Context` is null — non-wildcard resources always pass)
- [x] 6.3 Track `discoveredCount`, `filteredCount`, `addedCount` per `ResourceInstance` and emit a single `LogInformation` summary after processing all expanded resources for that instance

## 7. Tests

- [x] 7.1 In `Configuration.PrepareAndValidate` tests (new or existing): assert a valid `ResourceFilter` compiles without error; assert an invalid expression throws at startup with a descriptive message
- [x] 7.2 Create `ResourceManagerTests.cs`: mock `IResourceStateFactory.ExpandResourcesAsync` to return a mix of `ExpandedResource` entries with different contexts; assert only resources passing the filter are passed to `Create()`; assert the summary log counts (discovered / filtered / added) are correct
- [x] 7.3 In `ResourceManagerTests.cs`: assert that when `ResourceFilter` is null, all expanded resources are created (no regression)
- [x] 7.4 In `ResourceManagerTests.cs`: assert that when `Context` is null (non-wildcard resource), the filter is skipped and the resource is always created

## 8. Documentation

- [x] 8.1 Update `config.yml.example` to add a commented `ResourceFilter` example under the SQL database resource block showing the DTU filter pattern
