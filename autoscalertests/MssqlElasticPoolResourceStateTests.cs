using System.Reflection;
using Azure.ResourceManager.Sql.Models;
using Microsoft.Extensions.Logging;
using Moq;
using poolautoscaler.configuration;
using poolautoscaler.resources.MssqlElasticPool;
using poolautoscaler.resources.MssqlElasticPool.Dto;

namespace poolautoscaler.tests
{
    public class MssqlElasticPoolResourceStateTests
    {
        private readonly Mock<ILogger> loggerMock;
        private readonly Resource config;
        private readonly string resourceId;

        public MssqlElasticPoolResourceStateTests()
        {
            this.loggerMock = new Mock<ILogger>();
            this.resourceId = "/subscriptions/test/resourceGroups/test/providers/Microsoft.Sql/servers/test/elasticPools/test";
            this.config = new Resource { };
        }

        [Fact]
        public void SetDtuCapacity_WhenCurrentIsLower_ShouldUpdateCapacity()
        {
            // Arrange
            var state = new MssqlElasticPoolResourceState(this.resourceId, this.loggerMock.Object, this.config);

            state.ExistingMssqlElasticPoolState = new MssqlElasticPoolState
            {
                Sku = new SqlSku("StandardPool") { Capacity = 50 },
                MaxSizeBytes = 107374182400 // 100GB
            };

            state.RequestedMssqlElasticPoolState = new MssqlElasticPoolState();

            // Act
            state.SetDtuCapacity(100);

            // Assert
            var patch = state.PreparePatch();
            Assert.True(patch.HasChanges);
            Assert.True(patch.Disruptive);
            var patchData = (MssqlElasticPoolState)patch.PatchData;
            Assert.Equal(100, patchData.Sku.Capacity);
        }

        [Fact]
        public void SetDtuCapacity_WithMultipleUpdates_ShouldKeepHighestValue()
        {
            // Arrange
            var state = new MssqlElasticPoolResourceState(this.resourceId, this.loggerMock.Object, this.config);

            state.ExistingMssqlElasticPoolState = new MssqlElasticPoolState
            {
                Sku = new SqlSku("StandardPool") { Capacity = 50 },
                MaxSizeBytes = 107374182400 // 100GB
            };

            state.RequestedMssqlElasticPoolState = new MssqlElasticPoolState();

            // Act
            state.SetDtuCapacity(75);  // First update
            state.SetDtuCapacity(60);  // Should be ignored (lower)
            state.SetDtuCapacity(100); // Should be kept (highest)

            // Assert
            var patch = state.PreparePatch();
            Assert.True(patch.HasChanges);
            var patchData = (MssqlElasticPoolState)patch.PatchData;
            Assert.Equal(100, patchData.Sku.Capacity);
        }

        [Fact]
        public void SetMaxSizeBytes_WhenCurrentIsLower_ShouldUpdateSize()
        {
            // Arrange
            var state = new MssqlElasticPoolResourceState(this.resourceId, this.loggerMock.Object, this.config);

            state.ExistingMssqlElasticPoolState = new MssqlElasticPoolState
            {
                Sku = new SqlSku("StandardPool") { Capacity = 50 },
                MaxSizeBytes = 107374182400 // 100GB
            };

            state.RequestedMssqlElasticPoolState = new MssqlElasticPoolState();

            // Act
            state.SetMaxSizeBytes(214748364800); // 200GB

            // Assert
            var patch = state.PreparePatch();
            Assert.True(patch.HasChanges);
            var patchData = (MssqlElasticPoolState)patch.PatchData;
            Assert.Equal(214748364800, patchData.MaxSizeBytes);
        }

        [Fact]
        public void SetMaxSizeBytes_WhenNewSizeIsLower_ShouldKeepExistingSize()
        {
            // Arrange
            var state = new MssqlElasticPoolResourceState(this.resourceId, this.loggerMock.Object, this.config);

            state.ExistingMssqlElasticPoolState = new MssqlElasticPoolState
            {
                Sku = new SqlSku("StandardPool") { Capacity = 50 },
                CurrentUsedStorage = 214748364800,
                MaxSizeBytes = MssqlElasticPoolResourceStateHelper.FindClosesValidStorageSize(214748364800) // 200GB
            };

            state.RequestedMssqlElasticPoolState = new MssqlElasticPoolState();

            // Act
            state.SetMaxSizeBytes(107374182400); // 100GB - Should be ignored as it's lower

            // Assert
            var patch = state.PreparePatch();
            Assert.False(patch.HasChanges);
            var patchData = (MssqlElasticPoolState)patch.PatchData;
            Assert.Equal(214748364800, patchData.MaxSizeBytes);
        }

        [Fact]
        public void PreparePatch_WhenPoolDtuReducedBelowExistingPerDbMax_ShouldClampPerDbMaxToNewPoolDtu()
        {
            // Arrange: pool is currently 300 DTU; per-db max was set to 200 (valid under 300 DTU).
            // Pool DTU is now being scaled down to 100 DTU.  The per-db max of 200 would exceed
            // the new pool ceiling of 100 and cause ElasticPoolDbDtuMaxAboveLimit from Azure.
            var state = new MssqlElasticPoolResourceState(this.resourceId, this.loggerMock.Object, this.config);

            state.ExistingMssqlElasticPoolState = new MssqlElasticPoolState
            {
                Sku = new SqlSku("StandardPool") { Capacity = 300 },
                MaxSizeBytes = 536870912000, // 500 GB
                PerDatabaseMaxCapacity = 200,
            };

            state.RequestedMssqlElasticPoolState = new MssqlElasticPoolState
            {
                Sku = new SqlSku("StandardPool") { Capacity = 100 },
                PerDatabaseMaxCapacity = 200, // was valid under old 300-DTU pool
            };

            // Act
            var patch = state.PreparePatch();

            // Assert: per-db max must be clamped to the largest valid Standard value <= 100 DTU.
            Assert.True(patch.HasChanges);
            var patchData = (MssqlElasticPoolState)patch.PatchData;
            Assert.Equal(100, patchData.Sku.Capacity);
            Assert.Equal(100, patchData.PerDatabaseMaxCapacity);
        }

        [Fact]
        public void PreparePatch_WithCurrentStorageUsage_ShouldNormalizeToValidSize()
        {
            // Arrange
            var state = new MssqlElasticPoolResourceState(this.resourceId, this.loggerMock.Object, this.config);

            state.ExistingMssqlElasticPoolState = new MssqlElasticPoolState
            {
                Sku = new SqlSku("StandardPool") { Capacity = 50 },
                MaxSizeBytes = 107374182400, // 100GB
                CurrentUsedStorage = 150000000000 // 150GB - Non-standard size
            };

            state.RequestedMssqlElasticPoolState = new MssqlElasticPoolState();

            // Act
            state.SetMaxSizeBytes(160000000000); // Non-standard size

            // Assert
            var patch = state.PreparePatch();
            Assert.True(patch.HasChanges);
            var patchData = (MssqlElasticPoolState)patch.PatchData;

            // The actual value will be normalized to a valid storage size
            Assert.NotEqual(160000000000, patchData.MaxSizeBytes);
            Assert.True(patchData.MaxSizeBytes >= state.ExistingMssqlElasticPoolState.CurrentUsedStorage);
        }

        // Guard 1: CurrentUsedStorage = 0 suppression
        [Fact]
        public void PreparePatch_WhenCurrentUsedStorageIsZeroAndExistingMaxSizeBytesIsSet_ShouldSuppressStorageReduction()
        {
            // Arrange: pool reports CurrentUsedStorage=0 (broken metric) while having a 300 GB MaxSizeBytes.
            // A rule targets 100 GB — Guard 1 should clamp the patch back to 300 GB.
            var state = new MssqlElasticPoolResourceState(this.resourceId, this.loggerMock.Object, this.config);

            const long existingMax = 322122547200L; // 300 GB

            state.ExistingMssqlElasticPoolState = new MssqlElasticPoolState
            {
                Sku = new SqlSku("StandardPool") { Capacity = 100 },
                MaxSizeBytes = existingMax,
                CurrentUsedStorage = 0,
            };

            state.RequestedMssqlElasticPoolState = new MssqlElasticPoolState();
            state.SetMaxSizeBytes(107374182400L); // rule targets 100 GB — a reduction

            // Act
            var patch = state.PreparePatch();

            // Assert: reduction suppressed; patch stays at existing max (or above via tier snap)
            var patchData = (MssqlElasticPoolState)patch.PatchData;
            Assert.True(
                patchData.MaxSizeBytes >= existingMax,
                $"Expected MaxSizeBytes >= {existingMax} (existing) but got {patchData.MaxSizeBytes}");
        }

        [Fact]
        public void PreparePatch_WhenCurrentUsedStorageIsZeroAndRuleRequestsScaleUp_ShouldAllowIncrease()
        {
            // Arrange: same broken-metric scenario but the rule targets a size above the existing max.
            // Guard 1 must NOT block increases.
            var state = new MssqlElasticPoolResourceState(this.resourceId, this.loggerMock.Object, this.config);

            const long existingMax = 107374182400L; // 100 GB

            state.ExistingMssqlElasticPoolState = new MssqlElasticPoolState
            {
                Sku = new SqlSku("StandardPool") { Capacity = 100 },
                MaxSizeBytes = existingMax,
                CurrentUsedStorage = 0,
            };

            state.RequestedMssqlElasticPoolState = new MssqlElasticPoolState();
            state.SetMaxSizeBytes(214748364800L); // rule targets 200 GB — an increase

            // Act
            var patch = state.PreparePatch();

            // Assert: increase is allowed
            var patchData = (MssqlElasticPoolState)patch.PatchData;
            Assert.True(
                patchData.MaxSizeBytes >= 214748364800L,
                $"Expected MaxSizeBytes >= 200 GB but got {patchData.MaxSizeBytes}");
        }

        // Recovery bump applied by PreparePatch
        [Fact]
        public void PreparePatch_WhenRecoveryBumpIsSet_ShouldAddBumpToComputedMaxSizeBytes()
        {
            // Arrange: simulate a stuck pool where ApplyChanges has already incremented the bump
            // by one step (50 GB). PreparePatch should add the bump to the formula result.
            var state = new MssqlElasticPoolResourceState(this.resourceId, this.loggerMock.Object, this.config);

            const long existingMax = 107374182400L; // 100 GB
            const long bumpBytes = 50L * 1024L * 1024L * 1024L; // 50 GB

            state.ExistingMssqlElasticPoolState = new MssqlElasticPoolState
            {
                Sku = new SqlSku("StandardPool") { Capacity = 100 },
                MaxSizeBytes = existingMax,
                CurrentUsedStorage = existingMax, // pool is at 100% (stuck state)
            };

            state.RequestedMssqlElasticPoolState = new MssqlElasticPoolState();
            state.SetMaxSizeBytes(existingMax); // rule also targets existing max (no change from formula)

            // Use reflection to simulate the bump that ApplyChanges would have accumulated.
            SetBumpBytesViaReflection(state, bumpBytes);

            // Act
            var patch = state.PreparePatch();

            // Assert: patch.MaxSizeBytes must be at least existingMax + bump (after tier snap)
            var patchData = (MssqlElasticPoolState)patch.PatchData;
            Assert.True(
                patchData.MaxSizeBytes >= existingMax + bumpBytes,
                $"Expected MaxSizeBytes >= {existingMax + bumpBytes} but got {patchData.MaxSizeBytes}");
        }

        [Fact]
        public void PreparePatch_WhenBumpIsSet_ShouldRetainBumpRegardlessOfCapacityLevel()
        {
            // Arrange: bump is retained by PreparePatch regardless of current usage.
            // It is only cleared by a successful ApplyChanges, not by a metric heuristic.
            var state = new MssqlElasticPoolResourceState(this.resourceId, this.loggerMock.Object, this.config);

            const long existingMax = 322122547200L; // 300 GB
            const long usedStorage = 214748364800L; // 200 GB (~66% — well under 95%)
            const long bumpBytes = 50L * 1024L * 1024L * 1024L;

            state.ExistingMssqlElasticPoolState = new MssqlElasticPoolState
            {
                Sku = new SqlSku("StandardPool") { Capacity = 100 },
                MaxSizeBytes = existingMax,
                CurrentUsedStorage = usedStorage,
            };

            state.RequestedMssqlElasticPoolState = new MssqlElasticPoolState();
            state.SetMaxSizeBytes(existingMax);

            SetBumpBytesViaReflection(state, bumpBytes);

            // Act
            state.PreparePatch();

            // Assert: bump is retained; PreparePatch does not reset it
            Assert.Equal(bumpBytes, GetBumpBytesViaReflection(state));
        }

        // Helpers
        private static void SetBumpBytesViaReflection(MssqlElasticPoolResourceState state, long value)
        {
            var field = typeof(MssqlElasticPoolResourceState)
                .GetField("storageRecoveryBumpBytes", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(field);
            field!.SetValue(state, value);
        }

        private static long GetBumpBytesViaReflection(MssqlElasticPoolResourceState state)
        {
            var field = typeof(MssqlElasticPoolResourceState)
                .GetField("storageRecoveryBumpBytes", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(field);
            return (long)field!.GetValue(state)!;
        }
    }
}
