using Azure.ResourceManager.Sql.Models;
using Microsoft.Extensions.Logging;
using Moq;
using poolautoscaler.configuration;
using poolautoscaler.resources.MsSqlDatabase;
using poolautoscaler.resources.MsSqlDatabase.Dto;

namespace poolautoscaler.tests
{
    public class MsSqlDatabaseResourceStateTests
    {
        private readonly Mock<ILogger> loggerMock;
        private readonly Resource config;
        private readonly string resourceId;

        public MsSqlDatabaseResourceStateTests()
        {
            this.loggerMock = new Mock<ILogger>();
            this.resourceId = "/subscriptions/test/resourceGroups/test/providers/Microsoft.Sql/servers/test/databases/test";
            this.config = new Resource { };
        }

        [Fact]
        public void SetDtuCapacity_WhenCurrentIsLower_ShouldUpdateCapacity()
        {
            // Arrange
            var state = new MsSqlDatabaseResourceState(this.resourceId, this.loggerMock.Object, this.config);

            // Simulate what Refresh would do
            var initialState = new MsSqlDatabaseState
            {
                Sku = new SqlSku("Standard") { Capacity = 20 },
                MaxSizeBytes = 1073741824 // 1GB
            };

            state.ExistingMsSqlDatabaseState = initialState;
            state.RequestedMsSqlDatabaseState = new MsSqlDatabaseState();

            // Act
            state.SetDtuCapacity(40);

            // Assert
            var patch = state.PreparePatch();
            Assert.True(patch.HasChanges);
            Assert.True(patch.Disruptive); // DTU changes are disruptive
            var patchData = (MsSqlDatabaseState)patch.PatchData;
            Assert.Equal(50, patchData.Sku.Capacity);
        }

        [Fact]
        public void SetDtuCapacity_WithMultipleUpdates_ShouldKeepHighestValue()
        {
            // Arrange
            var state = new MsSqlDatabaseResourceState(this.resourceId, this.loggerMock.Object, this.config);

            var initialState = new MsSqlDatabaseState
            {
                Sku = new SqlSku("Standard") { Capacity = 20 },
                MaxSizeBytes = 1073741824
            };

            state.ExistingMsSqlDatabaseState = initialState;
            state.RequestedMsSqlDatabaseState = new MsSqlDatabaseState();

            // Act
            state.SetDtuCapacity(30);
            state.SetDtuCapacity(25); // Should be ignored as it's lower
            state.SetDtuCapacity(40); // Should be kept as it's highest

            // Assert
            var patch = state.PreparePatch();
            Assert.True(patch.HasChanges);
            var patchData = (MsSqlDatabaseState)patch.PatchData;
            Assert.Equal(50, patchData.Sku.Capacity);
        }

        [Fact]
        public void SetMaxSizeBytes_WhenCurrentIsLower_ShouldUpdateSize()
        {
            // Arrange
            var state = new MsSqlDatabaseResourceState(this.resourceId, this.loggerMock.Object, this.config);

            var initialState = new MsSqlDatabaseState
            {
                Sku = new SqlSku("Standard") { Capacity = 20 },
                MaxSizeBytes = 1073741824 // 1GB
            };

            state.ExistingMsSqlDatabaseState = initialState;
            state.RequestedMsSqlDatabaseState = new MsSqlDatabaseState();

            // Act
            state.SetMaxSizeBytes(2147483648); // 2GB

            // Assert
            var patch = state.PreparePatch();
            Assert.True(patch.HasChanges);
            var patchData = (MsSqlDatabaseState)patch.PatchData;
            Assert.Equal(2147483648, patchData.MaxSizeBytes);
        }

        [Fact]
        public void SetMaxSizeBytes_WhenNewSizeIsLower_ShouldKeepExistingSize()
        {
            // Arrange
            var state = new MsSqlDatabaseResourceState(this.resourceId, this.loggerMock.Object, this.config);

            var sku = new SqlSku("Standard") { Capacity = 20 };
            var initialState = new MsSqlDatabaseState
            {
                Sku = sku,
                CurrentUsedStorage = 2147483648,
                MaxSizeBytes = MsSqlDatabaseResourceStateHelper.FindClosestValidStorageSizeForDatabase(2147483648, sku)
            };

            state.ExistingMsSqlDatabaseState = initialState;
            state.RequestedMsSqlDatabaseState = new MsSqlDatabaseState();

            // Act
            state.SetMaxSizeBytes(1024 * 1024 * 1024); // 1GB - Should be ignored as it's lower than current usage

            // Assert
            var patch = state.PreparePatch();
            Assert.False(patch.HasChanges);
            var patchData = (MsSqlDatabaseState)patch.PatchData;
            Assert.Equal(2147483648, patchData.MaxSizeBytes);
        }

        [Fact]
        public void PreparePatch_WithNoChanges_ShouldReturnNoChanges()
        {
            // Arrange
            var state = new MsSqlDatabaseResourceState(this.resourceId, this.loggerMock.Object, this.config);

            var sku = new SqlSku("Standard") { Capacity = 20 };
            var initialState = new MsSqlDatabaseState
            {
                Sku = sku,
                MaxSizeBytes = MsSqlDatabaseResourceStateHelper.FindClosestValidStorageSizeForDatabase(1073741824, sku)
            };

            state.ExistingMsSqlDatabaseState = initialState;
            state.RequestedMsSqlDatabaseState = new MsSqlDatabaseState();

            // Act & Assert
            var patch = state.PreparePatch();
            Assert.False(patch.HasChanges);
        }

        [Fact]
        public void PreparePatch_ShouldNormalizeMaxSizeBytes()
        {
            // Arrange
            var state = new MsSqlDatabaseResourceState(this.resourceId, this.loggerMock.Object, this.config);

            var sku = new SqlSku("Standard") { Capacity = 20 };
            var initialState = new MsSqlDatabaseState
            {
                Sku = sku,
                MaxSizeBytes = MsSqlDatabaseResourceStateHelper.FindClosestValidStorageSizeForDatabase(1073741824, sku)
            };

            state.ExistingMsSqlDatabaseState = initialState;
            state.RequestedMsSqlDatabaseState = new MsSqlDatabaseState();

            // Act
            state.SetMaxSizeBytes(1500000000); // Non-standard size

            // Assert
            var patch = state.PreparePatch();
            Assert.True(patch.HasChanges);
            var patchData = (MsSqlDatabaseState)patch.PatchData;

            // The actual value will depend on FindClosestValidStorageSizeForDatabase
            Assert.NotEqual(1500000000, patchData.MaxSizeBytes);
        }
    }
}
