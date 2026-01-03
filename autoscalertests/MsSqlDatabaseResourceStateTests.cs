using Azure.ResourceManager.Sql.Models;
using Microsoft.Extensions.Logging;
using Moq;
using poolautoscaler.resources;

namespace poolautoscaler.tests
{
    public class MsSqlDatabaseResourceStateTests
    {
        private readonly Mock<ILogger> _loggerMock;
        private readonly Resource _config;
        private readonly string _resourceId;

        public MsSqlDatabaseResourceStateTests()
        {
            _loggerMock = new Mock<ILogger>();
            _resourceId = "/subscriptions/test/resourceGroups/test/providers/Microsoft.Sql/servers/test/databases/test";
            _config = new Resource { };
        }

        [Fact]
        public void SetDtuCapacity_WhenCurrentIsLower_ShouldUpdateCapacity()
        {
            // Arrange
            var state = new MsSqlDatabaseResourceState(_resourceId, _loggerMock.Object, _config);

            // Simulate what Refresh would do
            var initialState = new MsSqlDatabaseResourceState.MsSqlDatabaseState
            {
                Sku = new SqlSku("Standard") { Capacity = 20 },
                MaxSizeBytes = 1073741824 // 1GB
            };

            state.ExistingMsSqlDatabaseState = initialState;
            state.RequestedMsSqlDatabaseState = new MsSqlDatabaseResourceState.MsSqlDatabaseState();

            // Act
            state.SetDtuCapacity(40);

            // Assert
            var patch = state.PreparePatch();
            Assert.True(patch.HasChanges);
            Assert.True(patch.Disruptive); // DTU changes are disruptive
            var patchData = (MsSqlDatabaseResourceState.MsSqlDatabaseState)patch.PatchData;
            Assert.Equal(50, patchData.Sku.Capacity);
        }

        [Fact]
        public void SetDtuCapacity_WithMultipleUpdates_ShouldKeepHighestValue()
        {
            // Arrange
            var state = new MsSqlDatabaseResourceState(_resourceId, _loggerMock.Object, _config);

            var initialState = new MsSqlDatabaseResourceState.MsSqlDatabaseState
            {
                Sku = new SqlSku("Standard") { Capacity = 20 },
                MaxSizeBytes = 1073741824
            };

            state.ExistingMsSqlDatabaseState = initialState;
            state.RequestedMsSqlDatabaseState = new MsSqlDatabaseResourceState.MsSqlDatabaseState();

            // Act
            state.SetDtuCapacity(30);
            state.SetDtuCapacity(25); // Should be ignored as it's lower
            state.SetDtuCapacity(40); // Should be kept as it's highest

            // Assert
            var patch = state.PreparePatch();
            Assert.True(patch.HasChanges);
            var patchData = (MsSqlDatabaseResourceState.MsSqlDatabaseState)patch.PatchData;
            Assert.Equal(50, patchData.Sku.Capacity);
        }

        [Fact]
        public void SetMaxSizeBytes_WhenCurrentIsLower_ShouldUpdateSize()
        {
            // Arrange
            var state = new MsSqlDatabaseResourceState(_resourceId, _loggerMock.Object, _config);

            var initialState = new MsSqlDatabaseResourceState.MsSqlDatabaseState
            {
                Sku = new SqlSku("Standard") { Capacity = 20 },
                MaxSizeBytes = 1073741824 // 1GB
            };

            state.ExistingMsSqlDatabaseState = initialState;
            state.RequestedMsSqlDatabaseState = new MsSqlDatabaseResourceState.MsSqlDatabaseState();

            // Act
            state.SetMaxSizeBytes(2147483648); // 2GB

            // Assert
            var patch = state.PreparePatch();
            Assert.True(patch.HasChanges);
            var patchData = (MsSqlDatabaseResourceState.MsSqlDatabaseState)patch.PatchData;
            Assert.Equal(2147483648, patchData.MaxSizeBytes);
        }

        [Fact]
        public void SetMaxSizeBytes_WhenNewSizeIsLower_ShouldKeepExistingSize()
        {
            // Arrange
            var state = new MsSqlDatabaseResourceState(_resourceId, _loggerMock.Object, _config);

            var sku = new SqlSku("Standard") { Capacity = 20 };
            var initialState = new MsSqlDatabaseResourceState.MsSqlDatabaseState
            {
                Sku = sku,
                CurrentUsedStorage = 2147483648,
                MaxSizeBytes = MsSqlDatabaseResourceStateHelper.FindClosestValidStorageSizeForDatabase(2147483648, sku)
            };

            state.ExistingMsSqlDatabaseState = initialState;
            state.RequestedMsSqlDatabaseState = new MsSqlDatabaseResourceState.MsSqlDatabaseState();

            // Act
            state.SetMaxSizeBytes(1024 * 1024 * 1024); // 1GB - Should be ignored as it's lower than current usage

            // Assert
            var patch = state.PreparePatch();
            Assert.False(patch.HasChanges);
            var patchData = (MsSqlDatabaseResourceState.MsSqlDatabaseState)patch.PatchData;
            Assert.Equal(2147483648, patchData.MaxSizeBytes);
        }

        [Fact]
        public void PreparePatch_WithNoChanges_ShouldReturnNoChanges()
        {
            // Arrange
            var state = new MsSqlDatabaseResourceState(_resourceId, _loggerMock.Object, _config);

            var sku = new SqlSku("Standard") { Capacity = 20 };
            var initialState = new MsSqlDatabaseResourceState.MsSqlDatabaseState
            {
                Sku = sku,
                MaxSizeBytes = MsSqlDatabaseResourceStateHelper.FindClosestValidStorageSizeForDatabase(1073741824, sku)
            };

            state.ExistingMsSqlDatabaseState = initialState;
            state.RequestedMsSqlDatabaseState = new MsSqlDatabaseResourceState.MsSqlDatabaseState();

            // Act & Assert
            var patch = state.PreparePatch();
            Assert.False(patch.HasChanges);
        }

        [Fact]
        public void PreparePatch_ShouldNormalizeMaxSizeBytes()
        {
            // Arrange
            var state = new MsSqlDatabaseResourceState(_resourceId, _loggerMock.Object, _config);

            var sku = new SqlSku("Standard") { Capacity = 20 };
            var initialState = new MsSqlDatabaseResourceState.MsSqlDatabaseState
            {
                Sku = sku,
                MaxSizeBytes = MsSqlDatabaseResourceStateHelper.FindClosestValidStorageSizeForDatabase(1073741824, sku)
            };

            state.ExistingMsSqlDatabaseState = initialState;
            state.RequestedMsSqlDatabaseState = new MsSqlDatabaseResourceState.MsSqlDatabaseState();

            // Act
            state.SetMaxSizeBytes(1500000000); // Non-standard size

            // Assert
            var patch = state.PreparePatch();
            Assert.True(patch.HasChanges);
            var patchData = (MsSqlDatabaseResourceState.MsSqlDatabaseState)patch.PatchData;
            // The actual value will depend on FindClosestValidStorageSizeForDatabase
            Assert.NotEqual(1500000000, patchData.MaxSizeBytes);
        }
    }
}