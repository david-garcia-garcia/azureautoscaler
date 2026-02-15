using Microsoft.Extensions.Logging;
using Moq;
using poolautoscaler.configuration;
using poolautoscaler.resources.FabricCapacity;
using poolautoscaler.resources.FabricCapacity.Dto;

namespace poolautoscaler.tests
{
    public class FabricCapacityResourceStateTests
    {
        private readonly Mock<ILogger> loggerMock;
        private readonly Resource config;
        private readonly string resourceId;

        public FabricCapacityResourceStateTests()
        {
            this.loggerMock = new Mock<ILogger>();
            this.resourceId = "/subscriptions/test/resourceGroups/test/providers/Microsoft.Fabric/capacities/test";
            this.config = new Resource { };
        }

        [Fact]
        public void SetSku_WhenCurrentIsLower_ShouldUpdateSku()
        {
            // Arrange
            var state = new FabricCapacityResourceState(this.resourceId, this.loggerMock.Object, this.config);

            state.ExistingFabricCapacityState = new FabricCapacityState
            {
                Sku = "F2"
            };

            state.RequestedFabricCapacityState = new FabricCapacityState();

            // Act
            state.SetSku("F4");

            // Assert
            var patch = state.PreparePatch();
            Assert.True(patch.HasChanges);
            Assert.True(patch.Disruptive);
            var patchData = (FabricCapacityState)patch.PatchData;
            Assert.Equal("F4", patchData.Sku);
        }

        [Fact]
        public void SetSku_WithMultipleUpdates_ShouldKeepHighestValue()
        {
            // Arrange
            var state = new FabricCapacityResourceState(this.resourceId, this.loggerMock.Object, this.config);

            state.ExistingFabricCapacityState = new FabricCapacityState
            {
                Sku = "F2"
            };

            state.RequestedFabricCapacityState = new FabricCapacityState();

            // Act
            state.SetSku("F4");  // First update
            state.SetSku("F2");  // Should be ignored (lower)
            state.SetSku("F8");  // Should be kept (highest)

            // Assert
            var patch = state.PreparePatch();
            Assert.True(patch.HasChanges);
            var patchData = (FabricCapacityState)patch.PatchData;
            Assert.Equal("F8", patchData.Sku);
        }

        [Fact]
        public void SetSku_WhenNewSkuIsLower_ShouldKeepExistingSku()
        {
            // Arrange
            var state = new FabricCapacityResourceState(this.resourceId, this.loggerMock.Object, this.config);

            state.ExistingFabricCapacityState = new FabricCapacityState
            {
                Sku = "F8"
            };

            state.RequestedFabricCapacityState = new FabricCapacityState
            {
                Sku = "F8" // Already set to F8
            };

            // Act
            state.SetSku("F4"); // Should be ignored as it's lower than requested F8

            // Assert
            var patch = state.PreparePatch();
            Assert.False(patch.HasChanges); // No changes because requested (F8) equals existing (F8)
            var patchData = (FabricCapacityState)patch.PatchData;
            Assert.Equal("F8", patchData.Sku);
        }

        [Fact]
        public void PreparePatch_WhenNoChanges_ShouldReturnNoChanges()
        {
            // Arrange
            var state = new FabricCapacityResourceState(this.resourceId, this.loggerMock.Object, this.config);

            state.ExistingFabricCapacityState = new FabricCapacityState
            {
                Sku = "F4"
            };

            state.RequestedFabricCapacityState = new FabricCapacityState();

            // Act
            var patch = state.PreparePatch();

            // Assert
            Assert.False(patch.HasChanges);
            Assert.False(patch.Disruptive);
            var patchData = (FabricCapacityState)patch.PatchData;
            Assert.Equal("F4", patchData.Sku);
        }

        [Fact]
        public void PreparePatch_WhenRequestedSkuIsNull_ShouldUseExistingSku()
        {
            // Arrange
            var state = new FabricCapacityResourceState(this.resourceId, this.loggerMock.Object, this.config);

            state.ExistingFabricCapacityState = new FabricCapacityState
            {
                Sku = "F4"
            };

            state.RequestedFabricCapacityState = new FabricCapacityState();

            // Act
            var patch = state.PreparePatch();

            // Assert
            Assert.False(patch.HasChanges);
            var patchData = (FabricCapacityState)patch.PatchData;
            Assert.Equal("F4", patchData.Sku);
        }

        [Fact]
        public void Constructor_WithInvalidResourceId_ShouldThrowArgumentException()
        {
            // Arrange
            var invalidResourceId = "/subscriptions/test/resourceGroups/test/providers/Microsoft.Sql/servers/test";

            // Act & Assert
            Assert.Throws<ArgumentException>(() =>
                new FabricCapacityResourceState(invalidResourceId, this.loggerMock.Object, this.config));
        }
    }
}
