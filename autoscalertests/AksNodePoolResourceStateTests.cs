using Azure.ResourceManager.ContainerService;
using Microsoft.Extensions.Logging;
using Moq;
using poolautoscaler.resources;

namespace poolautoscaler.tests
{
    public class AksNodePoolResourceStateTests
    {
        private readonly Mock<ILogger> _loggerMock;
        private readonly Resource _config;
        private readonly string _resourceId;

        public AksNodePoolResourceStateTests()
        {
            _loggerMock = new Mock<ILogger>();
            _resourceId = "/subscriptions/test/resourceGroups/test/providers/Microsoft.ContainerService/managedClusters/test/agentPools/test";
        }

        [Fact]
        public void SetMinNodeCount_WhenCurrentIsLower_ShouldUpdatePendingCount()
        {
            // Arrange
            var state = new AksNodePoolResourceState(_resourceId, _loggerMock.Object, _config);

            // Simulate what Refresh would do
            AksNodePoolResourceState.AksNodePoolState initialState = new AksNodePoolResourceState.AksNodePoolState()
            {
                MaxNodeCount = 6,
                MinNodeCount = 2
            };

            state.ExistingAksNodePoolState = initialState;
            state.RequestedAksNodePoolState = new AksNodePoolResourceState.AksNodePoolState();

            // Act
            state.SetMinNodeCount(3); // Try to set to 4 when current is 2

            // Assert
            var patch = state.PreparePatch();
            Assert.True(patch.HasChanges);
            Assert.Equal(3, ((AksNodePoolResourceState.AksNodePoolState)patch.PatchData).MinNodeCount);
            Assert.Equal(6, ((AksNodePoolResourceState.AksNodePoolState)patch.PatchData).MaxNodeCount);
        }

        [Fact]
        public void SetMinNodeCount_WhenExceedsMaxCount_ShouldUpdateMaxCount()
        {
            // Arrange
            var state = new AksNodePoolResourceState(_resourceId, _loggerMock.Object, _config);

            // Simulate what Refresh would do
            AksNodePoolResourceState.AksNodePoolState initialState = new AksNodePoolResourceState.AksNodePoolState()
            {
                MaxNodeCount = 3,
                MinNodeCount = 2
            };

            state.ExistingAksNodePoolState = initialState;
            state.RequestedAksNodePoolState = new AksNodePoolResourceState.AksNodePoolState();

            // Act
            state.SetMinNodeCount(4); // Try to set to 4 when current is 2

            // Assert
            var patch = state.PreparePatch();
            Assert.True(patch.HasChanges);
            Assert.Equal(4, ((AksNodePoolResourceState.AksNodePoolState)patch.PatchData).MinNodeCount);
            Assert.Equal(4, ((AksNodePoolResourceState.AksNodePoolState)patch.PatchData).MaxNodeCount);
        }

        [Fact]
        public void SetMinNodeCount_WithMultipleUpdates_ShouldKeepHighestValue()
        {
            // Arrange
            var state = new AksNodePoolResourceState(_resourceId, _loggerMock.Object, _config);

            // Simulate what Refresh would do
            AksNodePoolResourceState.AksNodePoolState initialState = new AksNodePoolResourceState.AksNodePoolState()
            {
                MaxNodeCount = 12,
                MinNodeCount = 2
            };

            state.ExistingAksNodePoolState = initialState;
            state.RequestedAksNodePoolState = new AksNodePoolResourceState.AksNodePoolState();

            state.SetMinNodeCount(4);
            state.SetMinNodeCount(1);
            state.SetMinNodeCount(5);
            state.SetMinNodeCount(2);

            // Assert
            var patch = state.PreparePatch();
            Assert.True(patch.HasChanges);
            Assert.Equal(5, ((AksNodePoolResourceState.AksNodePoolState)patch.PatchData).MinNodeCount);
            Assert.Equal(12, ((AksNodePoolResourceState.AksNodePoolState)patch.PatchData).MaxNodeCount);
        }

        [Fact]
        public void ApplyChanges_WhatIfMode_ShouldNotUpdateResource()
        {
            // Arrange
            var state = new AksNodePoolResourceState(_resourceId, _loggerMock.Object, _config);

            // Simulate what Refresh would do
            AksNodePoolResourceState.AksNodePoolState initialState = new AksNodePoolResourceState.AksNodePoolState()
            {
                MaxNodeCount = 12,
                MinNodeCount = 2
            };

            state.ExistingAksNodePoolState = initialState;
            state.RequestedAksNodePoolState = new AksNodePoolResourceState.AksNodePoolState();

            // Assert
            var patch = state.PreparePatch();
            Assert.False(patch.HasChanges);
        }
    }
}