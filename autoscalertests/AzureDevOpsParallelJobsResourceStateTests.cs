using Microsoft.Extensions.Logging;
using Moq;
using poolautoscaler.configuration;
using poolautoscaler.resources.AzureDevops;
using poolautoscaler.resources.AzureDevops.Dto;

namespace poolautoscaler.tests
{
    public class AzureDevOpsParallelJobsResourceStateTests
    {
        private readonly Mock<ILogger> loggerMock;
        private readonly Resource config;
        private readonly string validResourceId;

        public AzureDevOpsParallelJobsResourceStateTests()
        {
            this.loggerMock = new Mock<ILogger>();
            this.validResourceId = "azuredevops://myorg";
            this.config = new Resource { };
        }

        [Fact]
        public void Constructor_WithValidResourceId_ShouldParseOrganization()
        {
            // Arrange & Act
            var state = this.CreateStateWithMockedClient(this.validResourceId);

            // Assert
            Assert.Equal("myorg", state.Organization);
        }

        [Fact]
        public void Constructor_WithInvalidResourceId_ShouldThrowArgumentException()
        {
            // Arrange
            var invalidResourceId = "/subscriptions/test/resourceGroups/test/providers/Microsoft.Sql/servers/test";

            // Act & Assert
            Assert.Throws<ArgumentException>(() =>
                this.CreateStateWithMockedClient(invalidResourceId));
        }

        [Fact]
        public void Constructor_WithTrailingSlash_ShouldThrowArgumentException()
        {
            // Arrange - Should not have trailing content
            var invalidResourceId = "azuredevops://myorg/extra";

            // Act & Assert
            Assert.Throws<ArgumentException>(() =>
                this.CreateStateWithMockedClient(invalidResourceId));
        }

        [Fact]
        public void SetHostedParallelJobs_ShouldUpdateRequestedState()
        {
            // Arrange
            var state = this.CreateStateWithMockedClient(this.validResourceId);
            state.ExistingParallelJobsState = new ParallelJobsState
            {
                HostedParallelJobs = 1,
                PrivateParallelJobs = 2
            };
            state.RequestedParallelJobsState = new ParallelJobsState();

            // Act
            state.SetHostedParallelJobs(5);

            // Assert
            Assert.Equal(5, state.RequestedParallelJobsState.HostedParallelJobs);
        }

        [Fact]
        public void SetHostedParallelJobs_WithMultipleUpdates_ShouldKeepHighestValue()
        {
            // Arrange
            var state = this.CreateStateWithMockedClient(this.validResourceId);
            state.ExistingParallelJobsState = new ParallelJobsState
            {
                HostedParallelJobs = 1
            };
            state.RequestedParallelJobsState = new ParallelJobsState();

            // Act
            state.SetHostedParallelJobs(5);
            state.SetHostedParallelJobs(3); // Lower value should be ignored
            state.SetHostedParallelJobs(10); // Higher value should be kept

            // Assert
            Assert.Equal(10, state.RequestedParallelJobsState.HostedParallelJobs);
        }

        [Fact]
        public void SetHostedParallelJobs_WithNegativeValue_ShouldThrowArgumentException()
        {
            // Arrange
            var state = this.CreateStateWithMockedClient(this.validResourceId);
            state.RequestedParallelJobsState = new ParallelJobsState();

            // Act & Assert
            Assert.Throws<ArgumentException>(() => state.SetHostedParallelJobs(-1));
        }

        [Fact]
        public void SetPrivateParallelJobs_ShouldUpdateRequestedState()
        {
            // Arrange
            var state = this.CreateStateWithMockedClient(this.validResourceId);
            state.ExistingParallelJobsState = new ParallelJobsState
            {
                HostedParallelJobs = 1,
                PrivateParallelJobs = 2
            };
            state.RequestedParallelJobsState = new ParallelJobsState();

            // Act
            state.SetPrivateParallelJobs(8);

            // Assert
            Assert.Equal(8, state.RequestedParallelJobsState.PrivateParallelJobs);
        }

        [Fact]
        public void PreparePatch_WhenHostedJobsChanged_ShouldReturnPatchWithChanges()
        {
            // Arrange
            var state = this.CreateStateWithMockedClient(this.validResourceId);
            state.ExistingParallelJobsState = new ParallelJobsState
            {
                HostedParallelJobs = 1,
                PrivateParallelJobs = 2
            };
            state.RequestedParallelJobsState = new ParallelJobsState();

            state.SetHostedParallelJobs(5);

            // Act
            var patch = state.PreparePatch();

            // Assert
            Assert.True(patch.HasChanges);
            Assert.False(patch.Disruptive);
            var patchData = (ParallelJobsState)patch.PatchData;
            Assert.Equal(5, patchData.HostedParallelJobs);
            Assert.Equal(2, patchData.PrivateParallelJobs); // Unchanged
        }

        [Fact]
        public void PreparePatch_WhenNoChanges_ShouldReturnPatchWithNoChanges()
        {
            // Arrange
            var state = this.CreateStateWithMockedClient(this.validResourceId);
            state.ExistingParallelJobsState = new ParallelJobsState
            {
                HostedParallelJobs = 5,
                PrivateParallelJobs = 2
            };
            state.RequestedParallelJobsState = new ParallelJobsState();

            // No changes requested

            // Act
            var patch = state.PreparePatch();

            // Assert
            Assert.False(patch.HasChanges);
            var patchData = (ParallelJobsState)patch.PatchData;
            Assert.Equal(5, patchData.HostedParallelJobs);
            Assert.Equal(2, patchData.PrivateParallelJobs);
        }

        [Fact]
        public void PreparePatch_WhenBothTypesChanged_ShouldReturnPatchWithAllChanges()
        {
            // Arrange
            var state = this.CreateStateWithMockedClient(this.validResourceId);
            state.ExistingParallelJobsState = new ParallelJobsState
            {
                HostedParallelJobs = 1,
                PrivateParallelJobs = 2
            };
            state.RequestedParallelJobsState = new ParallelJobsState();

            state.SetHostedParallelJobs(5);
            state.SetPrivateParallelJobs(10);

            // Act
            var patch = state.PreparePatch();

            // Assert
            Assert.True(patch.HasChanges);
            var patchData = (ParallelJobsState)patch.PatchData;
            Assert.Equal(5, patchData.HostedParallelJobs);
            Assert.Equal(10, patchData.PrivateParallelJobs);
        }

        [Fact]
        public void SetHostedParallelJobs_ToZero_ShouldBeAllowed()
        {
            // Arrange - Scale to zero is a key feature
            var state = this.CreateStateWithMockedClient(this.validResourceId);
            state.ExistingParallelJobsState = new ParallelJobsState
            {
                HostedParallelJobs = 5
            };
            state.RequestedParallelJobsState = new ParallelJobsState();

            // Act
            state.SetHostedParallelJobs(0);

            // Assert
            var patch = state.PreparePatch();
            Assert.True(patch.HasChanges);
            var patchData = (ParallelJobsState)patch.PatchData;
            Assert.Equal(0, patchData.HostedParallelJobs);
        }

        [Theory]
        [InlineData("azuredevops://org1")]
        [InlineData("azuredevops://my-org")]
        [InlineData("azuredevops://MyOrg123")]
        [InlineData("azuredevops://contoso")]
        public void Constructor_WithVariousValidResourceIds_ShouldSucceed(string resourceId)
        {
            // Act & Assert - Should not throw
            var state = this.CreateStateWithMockedClient(resourceId);
            Assert.NotNull(state);
        }

        private AzureDevOpsParallelJobsResourceState CreateStateWithMockedClient(string resourceId)
        {
            var clientMock = new Mock<AzureDevOpsClient>(this.loggerMock.Object);
            return new AzureDevOpsParallelJobsResourceState(resourceId, this.loggerMock.Object, this.config, clientMock.Object);
        }
    }
}
