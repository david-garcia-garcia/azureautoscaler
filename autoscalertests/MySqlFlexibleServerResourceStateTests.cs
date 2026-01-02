using Azure.ResourceManager.MySql.FlexibleServers.Models;
using Microsoft.Extensions.Logging;
using Moq;
using poolautoscaler.resources;

namespace poolautoscaler.tests
{
    public class MySqlFlexibleServerResourceStateTests
    {
        private readonly Mock<ILogger> _loggerMock;
        private readonly Resource _config;
        private readonly string _resourceId;

        public MySqlFlexibleServerResourceStateTests()
        {
            _loggerMock = new Mock<ILogger>();
            _resourceId = "/subscriptions/test/resourceGroups/test/providers/Microsoft.DBforMySQL/flexibleServers/test";
            _config = new Resource { };
        }

        [Fact]
        public void SetSku_WhenCurrentIsLower_ShouldUpdateSku()
        {
            // Arrange
            var state = new MySqlFlexibleServerResourceState(_resourceId, _loggerMock.Object, _config);

            state.ExistingMySqlFlexibleServerState = new MySqlFlexibleServerResourceState.MySqlFlexibleServerState
            {
                Sku = new MySqlFlexibleServerSku("Standard_B1ms", "Burstable"),
                CoreCount = 1
            };

            state.RequestedMySqlFlexibleServerState = new MySqlFlexibleServerResourceState.MySqlFlexibleServerState();

            // Act
            state.SetSku("Standard_B2ms"); // Higher SKU

            // Assert
            var patch = state.PreparePatch();
            Assert.True(patch.HasChanges);
            Assert.True(patch.Disruptive);
            var patchData = (MySqlFlexibleServerResourceState.MySqlFlexibleServerState)patch.PatchData;
            Assert.Equal("Standard_B2ms", patchData.Sku.Name);
        }

        [Fact]
        public void SetSku_WhenNewSkuIsLower_ShouldKeepExistingSku()
        {
            // Arrange
            var state = new MySqlFlexibleServerResourceState(_resourceId, _loggerMock.Object, _config);

            state.ExistingMySqlFlexibleServerState = new MySqlFlexibleServerResourceState.MySqlFlexibleServerState
            {
                Sku = new MySqlFlexibleServerSku("Standard_B2ms", "Burstable"),
                CoreCount = 2
            };

            state.RequestedMySqlFlexibleServerState = new MySqlFlexibleServerResourceState.MySqlFlexibleServerState();

            // Act
            state.SetSku("Standard_B1ms");

            // Assert
            var patch = state.PreparePatch();
            Assert.True(patch.HasChanges);
            var patchData = (MySqlFlexibleServerResourceState.MySqlFlexibleServerState)patch.PatchData;
            Assert.Equal("Standard_B1ms", patchData.Sku.Name);
        }

        [Fact]
        public void SetSku_SkuAndCpuPriority()
        {
            // Arrange
            var state = new MySqlFlexibleServerResourceState(_resourceId, _loggerMock.Object, _config);

            state.ExistingMySqlFlexibleServerState = new MySqlFlexibleServerResourceState.MySqlFlexibleServerState
            {
                Sku = new MySqlFlexibleServerSku("Standard_B2ms", "Burstable"),
                CoreCount = 2
            };

            state.RequestedMySqlFlexibleServerState = new MySqlFlexibleServerResourceState.MySqlFlexibleServerState();

            // Act
            state.SetSku("Standard_B1ms");
            state.SetCoreCount("4");

            // Assert
            var patch = state.PreparePatch();
            Assert.True(patch.HasChanges);
            var patchData = (MySqlFlexibleServerResourceState.MySqlFlexibleServerState)patch.PatchData;
            Assert.Equal("Standard_B4ms", patchData.Sku.Name);
        }

        [Fact]
        public void SetCoreCount_WhenCurrentIsLower_ShouldUpdateCoreCount()
        {
            // Arrange
            var state = new MySqlFlexibleServerResourceState(_resourceId, _loggerMock.Object, _config);

            state.ExistingMySqlFlexibleServerState = new MySqlFlexibleServerResourceState.MySqlFlexibleServerState
            {
                Sku = new MySqlFlexibleServerSku("Standard_B1ms", "Burstable"),
                CoreCount = 1
            };

            state.RequestedMySqlFlexibleServerState = new MySqlFlexibleServerResourceState.MySqlFlexibleServerState();

            // Act
            state.SetCoreCount("2"); // Higher core count

            // Assert
            var patch = state.PreparePatch();
            Assert.True(patch.HasChanges);
            Assert.True(patch.Disruptive);
            var patchData = (MySqlFlexibleServerResourceState.MySqlFlexibleServerState)patch.PatchData;
            Assert.Equal("Standard_B2s", patchData.Sku.Name);
        }

        [Fact]
        public void SetCoreCount_WithMultipleUpdates_ShouldKeepHighestValue()
        {
            // Arrange
            var state = new MySqlFlexibleServerResourceState(_resourceId, _loggerMock.Object, _config);

            state.ExistingMySqlFlexibleServerState = new MySqlFlexibleServerResourceState.MySqlFlexibleServerState
            {
                Sku = new MySqlFlexibleServerSku("Standard_B1ms", "Burstable"),
                CoreCount = 1
            };

            state.RequestedMySqlFlexibleServerState = new MySqlFlexibleServerResourceState.MySqlFlexibleServerState();

            // Act
            state.SetCoreCount("2");  // First update
            state.SetCoreCount("1");  // Should be ignored (lower)
            state.SetCoreCount("4");  // Should be kept (highest)

            // Assert
            var patch = state.PreparePatch();
            Assert.True(patch.HasChanges);
            Assert.True(patch.Disruptive);
            var patchData = (MySqlFlexibleServerResourceState.MySqlFlexibleServerState)patch.PatchData;
            Assert.Equal("Standard_B4ms", patchData.Sku.Name);
        }

        [Fact]
        public void PreparePatch_WithHigherCoreCount_ShouldUpdateToMinimumValidSku()
        {
            // Arrange
            var state = new MySqlFlexibleServerResourceState(_resourceId, _loggerMock.Object, _config);

            state.ExistingMySqlFlexibleServerState = new MySqlFlexibleServerResourceState.MySqlFlexibleServerState
            {
                Sku = new MySqlFlexibleServerSku("Standard_B1ms", "Burstable"),
                CoreCount = 1
            };

            state.RequestedMySqlFlexibleServerState = new MySqlFlexibleServerResourceState.MySqlFlexibleServerState();

            // Act
            state.SetCoreCount("4"); // Request more cores than current SKU supports

            // Assert
            var patch = state.PreparePatch();
            Assert.True(patch.HasChanges);
            Assert.True(patch.Disruptive);
            var patchData = (MySqlFlexibleServerResourceState.MySqlFlexibleServerState)patch.PatchData;
            // The actual SKU name will depend on MySqlFlexibleServerResourceStateHelper.GetMinimumSkuThatSatisfiesCoreCount
            Assert.NotEqual("Standard_B1ms", patchData.Sku.Name);
        }

        [Fact]
        public void PreparePatch_WithNoChanges_ShouldReturnNoChanges()
        {
            // Arrange
            var state = new MySqlFlexibleServerResourceState(_resourceId, _loggerMock.Object, _config);

            state.ExistingMySqlFlexibleServerState = new MySqlFlexibleServerResourceState.MySqlFlexibleServerState
            {
                Sku = new MySqlFlexibleServerSku("Standard_B1ms", "Burstable"),
                CoreCount = 1
            };

            state.RequestedMySqlFlexibleServerState = new MySqlFlexibleServerResourceState.MySqlFlexibleServerState();

            // Assert
            var patch = state.PreparePatch();
            Assert.False(patch.HasChanges);
            Assert.False(patch.Disruptive);
        }

        [Fact]
        public void SetIops_WhenCurrentIsLower_ShouldUpdateIops()
        {
            // Arrange
            var state = new MySqlFlexibleServerResourceState(_resourceId, _loggerMock.Object, _config);

            state.ExistingMySqlFlexibleServerState = new MySqlFlexibleServerResourceState.MySqlFlexibleServerState
            {
                Sku = new MySqlFlexibleServerSku("Standard_B1ms", "Burstable"),
                CoreCount = 1,
                Iops = 360
            };

            state.RequestedMySqlFlexibleServerState = new MySqlFlexibleServerResourceState.MySqlFlexibleServerState();

            // Act
            state.SetIops("500"); // Higher IOPS

            // Assert
            var patch = state.PreparePatch();
            Assert.True(patch.HasChanges);
            Assert.False(patch.Disruptive); // IOPS changes are not disruptive
            var patchData = (MySqlFlexibleServerResourceState.MySqlFlexibleServerState)patch.PatchData;
            Assert.Equal(500, patchData.Iops);
        }

        [Fact]
        public void SetIops_WhenExceedsCurrentSku_ShouldUpgradeSku()
        {
            // Arrange
            var state = new MySqlFlexibleServerResourceState(_resourceId, _loggerMock.Object, _config);

            state.ExistingMySqlFlexibleServerState = new MySqlFlexibleServerResourceState.MySqlFlexibleServerState
            {
                Sku = new MySqlFlexibleServerSku("Standard_B1ms", "Burstable"),
                CoreCount = 1,
                Iops = 360
            };

            state.RequestedMySqlFlexibleServerState = new MySqlFlexibleServerResourceState.MySqlFlexibleServerState();

            // Act
            state.SetIops("2000"); // IOPS too high for B1ms

            // Assert
            var patch = state.PreparePatch();
            Assert.True(patch.HasChanges);
            Assert.True(patch.Disruptive); // SKU change is disruptive
            var patchData = (MySqlFlexibleServerResourceState.MySqlFlexibleServerState)patch.PatchData;
            Assert.Equal("Standard_B4ms", patchData.Sku.Name); // Should upgrade to B4ms which supports 2400 IOPS
            Assert.Equal(2000, patchData.Iops);
        }

        [Fact]
        public void SetIops_AfterSettingCoreCount_ShouldNotDowngradeSku()
        {
            // Arrange
            var state = new MySqlFlexibleServerResourceState(_resourceId, _loggerMock.Object, _config);

            state.ExistingMySqlFlexibleServerState = new MySqlFlexibleServerResourceState.MySqlFlexibleServerState
            {
                Sku = new MySqlFlexibleServerSku("Standard_B1ms", "Burstable"),
                CoreCount = 1,
                Iops = 360
            };

            state.RequestedMySqlFlexibleServerState = new MySqlFlexibleServerResourceState.MySqlFlexibleServerState();

            // Act
            state.SetCoreCount("4"); // First set core count to 4
            state.SetIops("360"); // Then set IOPS that would require B4ms

            // Assert
            var patch = state.PreparePatch();
            Assert.True(patch.HasChanges);
            Assert.True(patch.Disruptive);
            var patchData = (MySqlFlexibleServerResourceState.MySqlFlexibleServerState)patch.PatchData;
            Assert.Equal("Standard_B4ms", patchData.Sku.Name); // Should keep B4ms for both core count and IOPS
            Assert.Equal(400, patchData.Iops);
        }

        [Fact]
        public void SetIops_WithMultipleUpdates_ShouldKeepHighestValue()
        {
            // Arrange
            var state = new MySqlFlexibleServerResourceState(_resourceId, _loggerMock.Object, _config);

            state.ExistingMySqlFlexibleServerState = new MySqlFlexibleServerResourceState.MySqlFlexibleServerState
            {
                Sku = new MySqlFlexibleServerSku("Standard_B1ms", "Burstable"),
                CoreCount = 1,
                Iops = 360
            };

            state.RequestedMySqlFlexibleServerState = new MySqlFlexibleServerResourceState.MySqlFlexibleServerState();

            // Act
            state.SetIops("500");  // First update
            state.SetIops("400");  // Should be ignored (lower)
            state.SetIops("600");  // Should be kept (highest)

            // Assert
            var patch = state.PreparePatch();
            Assert.True(patch.HasChanges);
            Assert.False(patch.Disruptive);
            var patchData = (MySqlFlexibleServerResourceState.MySqlFlexibleServerState)patch.PatchData;
            Assert.Equal(600, patchData.Iops);
        }
    }
}