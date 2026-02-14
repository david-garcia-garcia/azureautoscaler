using Microsoft.Extensions.Logging;
using Moq;
using poolautoscaler.configuration;
using poolautoscaler.resources.AksNodePool;
using poolautoscaler.resources.AksNodePool.Dto;

namespace poolautoscaler.tests
{
    public class AksNodePoolResourceStateTests
    {
        private readonly Mock<ILogger> loggerMock;
        private readonly Resource config;
        private readonly string resourceId;

        public AksNodePoolResourceStateTests()
        {
            this.loggerMock = new Mock<ILogger>();
            this.resourceId = "/subscriptions/test/resourceGroups/test/providers/Microsoft.ContainerService/managedClusters/test/agentPools/test";
            this.config = new Resource();
        }

        [Fact]
        public void SetMinNodeCount_WhenCurrentIsLower_ShouldUpdatePendingCount()
        {
            // Arrange
            var state = new AksNodePoolResourceState(this.resourceId, this.loggerMock.Object, this.config);

            // Simulate what Refresh would do
            AksNodePoolState initialState = new AksNodePoolState()
            {
                MaxNodeCount = 6,
                MinNodeCount = 2
            };

            state.ExistingAksNodePoolState = initialState;
            state.RequestedAksNodePoolState = new AksNodePoolState();

            // Act
            state.SetMinNodeCount(3); // Try to set to 4 when current is 2

            // Assert
            var patch = state.PreparePatch();
            Assert.True(patch.HasChanges);
            Assert.Equal(3, ((AksNodePoolState)patch.PatchData).MinNodeCount);
            Assert.Equal(6, ((AksNodePoolState)patch.PatchData).MaxNodeCount);
        }

        [Fact]
        public void SetMinNodeCount_WhenExceedsMaxCount_ShouldUpdateMaxCount()
        {
            // Arrange
            var state = new AksNodePoolResourceState(this.resourceId, this.loggerMock.Object, this.config);

            // Simulate what Refresh would do
            AksNodePoolState initialState = new AksNodePoolState()
            {
                MaxNodeCount = 3,
                MinNodeCount = 2
            };

            state.ExistingAksNodePoolState = initialState;
            state.RequestedAksNodePoolState = new AksNodePoolState();

            // Act
            state.SetMinNodeCount(4); // Try to set to 4 when current is 2

            // Assert
            var patch = state.PreparePatch();
            Assert.True(patch.HasChanges);
            Assert.Equal(4, ((AksNodePoolState)patch.PatchData).MinNodeCount);
            Assert.Equal(4, ((AksNodePoolState)patch.PatchData).MaxNodeCount);
        }

        [Fact]
        public void SetMinNodeCount_WithMultipleUpdates_ShouldKeepHighestValue()
        {
            // Arrange
            var state = new AksNodePoolResourceState(this.resourceId, this.loggerMock.Object, this.config);

            // Simulate what Refresh would do
            AksNodePoolState initialState = new AksNodePoolState()
            {
                MaxNodeCount = 12,
                MinNodeCount = 2
            };

            state.ExistingAksNodePoolState = initialState;
            state.RequestedAksNodePoolState = new AksNodePoolState();

            state.SetMinNodeCount(4);
            state.SetMinNodeCount(1);
            state.SetMinNodeCount(5);
            state.SetMinNodeCount(2);

            // Assert
            var patch = state.PreparePatch();
            Assert.True(patch.HasChanges);
            Assert.Equal(5, ((AksNodePoolState)patch.PatchData).MinNodeCount);
            Assert.Equal(12, ((AksNodePoolState)patch.PatchData).MaxNodeCount);
        }

        [Fact]
        public void ApplyChanges_WhatIfMode_ShouldNotUpdateResource()
        {
            // Arrange
            var state = new AksNodePoolResourceState(this.resourceId, this.loggerMock.Object, this.config);

            // Simulate what Refresh would do
            AksNodePoolState initialState = new AksNodePoolState()
            {
                MaxNodeCount = 12,
                MinNodeCount = 2
            };

            state.ExistingAksNodePoolState = initialState;
            state.RequestedAksNodePoolState = new AksNodePoolState();

            // Assert
            var patch = state.PreparePatch();
            Assert.False(patch.HasChanges);
        }
    }
}
