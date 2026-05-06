## 1. Core fix

- [x] 1.1 In `MssqlElasticPoolResourceState.PreparePatch`, after `patch.PerDatabaseMaxCapacity` is assigned and `patch.Sku.Capacity` is finalized, re-clamp with `MssqlElasticPoolResourceStateHelper.SnapToNearestPerDbMaxCapacity(patch.Sku, (int)patch.Sku.Capacity.Value, (int)patch.PerDatabaseMaxCapacity.Value)` when both values are present.

## 2. Verification

- [x] 2.1 Add regression test `PreparePatch_WhenPoolDtuReducedBelowExistingPerDbMax_ShouldClampPerDbMaxToNewPoolDtu` in `MssqlElasticPoolResourceStateTests`.
- [x] 2.2 Run `dotnet test` for the test project and confirm all tests pass.
