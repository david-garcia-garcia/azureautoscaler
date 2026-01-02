using Azure.ResourceManager.Sql.Models;
using Microsoft.Extensions.Logging;
using Moq;
using poolautoscaler.resources;

namespace poolautoscaler.tests
{
    public class MssqlElasticPoolResourceStateTests
    {
        private readonly Mock<ILogger> _loggerMock;
        private readonly Resource _config;
        private readonly string _resourceId;

        public MssqlElasticPoolResourceStateTests()
        {
            _loggerMock = new Mock<ILogger>();
            _resourceId = "/subscriptions/test/resourceGroups/test/providers/Microsoft.Sql/servers/test/elasticPools/test";
            _config = new Resource { };
        }

        [Fact]
        public void SetDtuCapacity_WhenCurrentIsLower_ShouldUpdateCapacity()
        {
            // Arrange
            var state = new MssqlElasticPoolResourceState(_resourceId, _loggerMock.Object, _config);

            state.ExistingMssqlElasticPoolState = new MssqlElasticPoolResourceState.MssqlElasticPoolState
            {
                Sku = new SqlSku("StandardPool") { Capacity = 50 },
                MaxSizeBytes = 107374182400 // 100GB
            };

            state.RequestedMssqlElasticPoolState = new MssqlElasticPoolResourceState.MssqlElasticPoolState();

            // Act
            state.SetDtuCapacity(100);

            // Assert
            var patch = state.PreparePatch();
            Assert.True(patch.HasChanges);
            Assert.True(patch.Disruptive);
            var patchData = (MssqlElasticPoolResourceState.MssqlElasticPoolState)patch.PatchData;
            Assert.Equal(100, patchData.Sku.Capacity);
        }

        [Fact]
        public void SetDtuCapacity_WithMultipleUpdates_ShouldKeepHighestValue()
        {
            // Arrange
            var state = new MssqlElasticPoolResourceState(_resourceId, _loggerMock.Object, _config);

            state.ExistingMssqlElasticPoolState = new MssqlElasticPoolResourceState.MssqlElasticPoolState
            {
                Sku = new SqlSku("StandardPool") { Capacity = 50 },
                MaxSizeBytes = 107374182400 // 100GB
            };

            state.RequestedMssqlElasticPoolState = new MssqlElasticPoolResourceState.MssqlElasticPoolState();

            // Act
            state.SetDtuCapacity(75);  // First update
            state.SetDtuCapacity(60);  // Should be ignored (lower)
            state.SetDtuCapacity(100); // Should be kept (highest)

            // Assert
            var patch = state.PreparePatch();
            Assert.True(patch.HasChanges);
            var patchData = (MssqlElasticPoolResourceState.MssqlElasticPoolState)patch.PatchData;
            Assert.Equal(100, patchData.Sku.Capacity);
        }

        [Fact]
        public void SetMaxSizeBytes_WhenCurrentIsLower_ShouldUpdateSize()
        {
            // Arrange
            var state = new MssqlElasticPoolResourceState(_resourceId, _loggerMock.Object, _config);

            state.ExistingMssqlElasticPoolState = new MssqlElasticPoolResourceState.MssqlElasticPoolState
            {
                Sku = new SqlSku("StandardPool") { Capacity = 50 },
                MaxSizeBytes = 107374182400 // 100GB
            };

            state.RequestedMssqlElasticPoolState = new MssqlElasticPoolResourceState.MssqlElasticPoolState();

            // Act
            state.SetMaxSizeBytes(214748364800); // 200GB

            // Assert
            var patch = state.PreparePatch();
            Assert.True(patch.HasChanges);
            var patchData = (MssqlElasticPoolResourceState.MssqlElasticPoolState)patch.PatchData;
            Assert.Equal(214748364800, patchData.MaxSizeBytes);
        }

        [Fact]
        public void SetMaxSizeBytes_WhenNewSizeIsLower_ShouldKeepExistingSize()
        {
            // Arrange
            var state = new MssqlElasticPoolResourceState(_resourceId, _loggerMock.Object, _config);

            state.ExistingMssqlElasticPoolState = new MssqlElasticPoolResourceState.MssqlElasticPoolState
            {
                Sku = new SqlSku("StandardPool") { Capacity = 50 },
                CurrentUsedStorage = 214748364800,
                MaxSizeBytes = MssqlElasticPoolResourceStateHelper.FindClosesValidStorageSize(214748364800) // 200GB
            };

            state.RequestedMssqlElasticPoolState = new MssqlElasticPoolResourceState.MssqlElasticPoolState();

            // Act
            state.SetMaxSizeBytes(107374182400); // 100GB - Should be ignored as it's lower

            // Assert
            var patch = state.PreparePatch();
            Assert.False(patch.HasChanges);
            var patchData = (MssqlElasticPoolResourceState.MssqlElasticPoolState)patch.PatchData;
            Assert.Equal(214748364800, patchData.MaxSizeBytes);
        }

        [Fact]
        public void PreparePatch_WithCurrentStorageUsage_ShouldNormalizeToValidSize()
        {
            // Arrange
            var state = new MssqlElasticPoolResourceState(_resourceId, _loggerMock.Object, _config);

            state.ExistingMssqlElasticPoolState = new MssqlElasticPoolResourceState.MssqlElasticPoolState
            {
                Sku = new SqlSku("StandardPool") { Capacity = 50 },
                MaxSizeBytes = 107374182400, // 100GB
                CurrentUsedStorage = 150000000000 // 150GB - Non-standard size
            };

            state.RequestedMssqlElasticPoolState = new MssqlElasticPoolResourceState.MssqlElasticPoolState();

            // Act
            state.SetMaxSizeBytes(160000000000); // Non-standard size

            // Assert
            var patch = state.PreparePatch();
            Assert.True(patch.HasChanges);
            var patchData = (MssqlElasticPoolResourceState.MssqlElasticPoolState)patch.PatchData;
            // The actual value will be normalized to a valid storage size
            Assert.NotEqual(160000000000, patchData.MaxSizeBytes);
            Assert.True(patchData.MaxSizeBytes >= state.ExistingMssqlElasticPoolState.CurrentUsedStorage);
        }
    }
}