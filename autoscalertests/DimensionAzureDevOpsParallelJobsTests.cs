using Microsoft.Extensions.Logging;
using Moq;
using poolautoscaler.configuration;
using poolautoscaler.dimensions;
using poolautoscaler.resourcemanagement;
using poolautoscaler.resources.AzureDevops;
using poolautoscaler.resources.AzureDevops.Dto;

namespace poolautoscaler.tests
{
    public class DimensionAzureDevOpsParallelJobsTests
    {
        private readonly Mock<ILogger> loggerMock;
        private readonly Resource config;
        private readonly string resourceId;

        public DimensionAzureDevOpsParallelJobsTests()
        {
            this.loggerMock = new Mock<ILogger>();
            this.resourceId = "azuredevops://myorg";
            this.config = new Resource { };
        }

        [Fact]
        public void HostedDimension_CanApplyDimension_WithCorrectResourceAndDimension_ReturnsTrue()
        {
            // Arrange
            var dimension = new DimensionAzureDevOpsHostedParallelJobs();
            var state = this.CreateStateWithMockedClient();
            var rule = new ScalingRule { Dimension = "HostedParallelJobs" };

            // Act
            var result = dimension.CanApplyDimension(state, rule, this.loggerMock.Object);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void HostedDimension_CanApplyDimension_WithWrongDimension_ReturnsFalse()
        {
            // Arrange
            var dimension = new DimensionAzureDevOpsHostedParallelJobs();
            var state = this.CreateStateWithMockedClient();
            var rule = new ScalingRule { Dimension = "PrivateParallelJobs" };

            // Act
            var result = dimension.CanApplyDimension(state, rule, this.loggerMock.Object);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void HostedDimension_GetCurrentDimensionValue_ReturnsCorrectValue()
        {
            // Arrange
            var dimension = new DimensionAzureDevOpsHostedParallelJobs();
            var state = this.CreateStateWithMockedClient();
            state.ExistingParallelJobsState = new ParallelJobsState
            {
                HostedParallelJobs = 5
            };

            // Act
            var result = dimension.GetCurrentDimensionValue(state);

            // Assert
            Assert.Equal("5", result);
        }

        [Fact]
        public void HostedDimension_GetNextDimensionValue_IncrementsValue()
        {
            // Arrange
            var dimension = new DimensionAzureDevOpsHostedParallelJobs();
            var state = this.CreateStateWithMockedClient();

            // Act
            var result = dimension.GetNextDimensionValue(state, "5");

            // Assert
            Assert.Equal("6", result);
        }

        [Fact]
        public void HostedDimension_GetPreviousDimensionValue_DecrementsValue()
        {
            // Arrange
            var dimension = new DimensionAzureDevOpsHostedParallelJobs();
            var state = this.CreateStateWithMockedClient();

            // Act
            var result = dimension.GetPreviousDimensionValue(state, "5");

            // Assert
            Assert.Equal("4", result);
        }

        [Fact]
        public void HostedDimension_GetPreviousDimensionValue_DoesNotGoBelowZero()
        {
            // Arrange
            var dimension = new DimensionAzureDevOpsHostedParallelJobs();
            var state = this.CreateStateWithMockedClient();

            // Act
            var result = dimension.GetPreviousDimensionValue(state, "0");

            // Assert
            Assert.Equal("0", result);
        }

        [Fact]
        public void HostedDimension_Compare_ReturnsCorrectComparison()
        {
            // Arrange
            var dimension = new DimensionAzureDevOpsHostedParallelJobs();

            // Act & Assert
            Assert.Equal(1, dimension.Compare(null!, "10", "5"));
            Assert.Equal(-1, dimension.Compare(null!, "5", "10"));
            Assert.Equal(0, dimension.Compare(null!, "5", "5"));
        }

        [Fact]
        public async Task HostedDimension_SetDimensionValue_UpdatesState()
        {
            // Arrange
            var dimension = new DimensionAzureDevOpsHostedParallelJobs();
            var state = this.CreateStateWithMockedClient();
            state.ExistingParallelJobsState = new ParallelJobsState();
            state.RequestedParallelJobsState = new ParallelJobsState();

            // Act
            await dimension.SetDimensionValue(CancellationToken.None, state, this.loggerMock.Object, null!, "7");

            // Assert
            Assert.Equal(7, state.RequestedParallelJobsState.HostedParallelJobs);
        }

        [Fact]
        public void HostedDimension_ValidateRuleConfiguration_WithValidMin_DoesNotThrow()
        {
            // Arrange
            var dimension = new DimensionAzureDevOpsHostedParallelJobs();
            var rule = new ScalingRule { DimensionValueMin = "0", DimensionValueMax = "10" };

            // Act & Assert - Should not throw
            dimension.ValidateRuleConfiguration(rule);
        }

        [Fact]
        public void HostedDimension_ValidateRuleConfiguration_WithNegativeMin_Throws()
        {
            // Arrange
            var dimension = new DimensionAzureDevOpsHostedParallelJobs();
            var rule = new ScalingRule { DimensionValueMin = "-1" };

            // Act & Assert
            Assert.Throws<ArgumentException>(() => dimension.ValidateRuleConfiguration(rule));
        }

        [Fact]
        public void PrivateDimension_CanApplyDimension_WithCorrectResourceAndDimension_ReturnsTrue()
        {
            // Arrange
            var dimension = new DimensionAzureDevOpsPrivateParallelJobs();
            var state = this.CreateStateWithMockedClient();
            var rule = new ScalingRule { Dimension = "PrivateParallelJobs" };

            // Act
            var result = dimension.CanApplyDimension(state, rule, this.loggerMock.Object);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void PrivateDimension_CanApplyDimension_WithWrongDimension_ReturnsFalse()
        {
            // Arrange
            var dimension = new DimensionAzureDevOpsPrivateParallelJobs();
            var state = this.CreateStateWithMockedClient();
            var rule = new ScalingRule { Dimension = "HostedParallelJobs" };

            // Act
            var result = dimension.CanApplyDimension(state, rule, this.loggerMock.Object);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void PrivateDimension_GetCurrentDimensionValue_ReturnsCorrectValue()
        {
            // Arrange
            var dimension = new DimensionAzureDevOpsPrivateParallelJobs();
            var state = this.CreateStateWithMockedClient();
            state.ExistingParallelJobsState = new ParallelJobsState
            {
                PrivateParallelJobs = 3
            };

            // Act
            var result = dimension.GetCurrentDimensionValue(state);

            // Assert
            Assert.Equal("3", result);
        }

        [Fact]
        public async Task PrivateDimension_SetDimensionValue_UpdatesState()
        {
            // Arrange
            var dimension = new DimensionAzureDevOpsPrivateParallelJobs();
            var state = this.CreateStateWithMockedClient();
            state.ExistingParallelJobsState = new ParallelJobsState();
            state.RequestedParallelJobsState = new ParallelJobsState();

            // Act
            await dimension.SetDimensionValue(CancellationToken.None, state, this.loggerMock.Object, null!, "12");

            // Assert
            Assert.Equal(12, state.RequestedParallelJobsState.PrivateParallelJobs);
        }

        [Fact]
        public void ResourceStateFactory_AzureDevOpsParallelJobsRegex_MatchesValidPattern()
        {
            // Arrange
            var validId = "azuredevops://myorg";

            // Act
            var match = ResourceStateFactory.AzureDevOpsParallelJobs.Match(validId);

            // Assert
            Assert.True(match.Success);
            Assert.Equal("myorg", match.Groups["organization"].Value);
        }

        [Fact]
        public void ResourceStateFactory_AzureDevOpsParallelJobsRegex_DoesNotMatchArmResources()
        {
            // Arrange
            var armResourceId = "/subscriptions/test/resourceGroups/test/providers/Microsoft.Sql/servers/test";

            // Act
            var match = ResourceStateFactory.AzureDevOpsParallelJobs.Match(armResourceId);

            // Assert
            Assert.False(match.Success);
        }

        [Theory]
        [InlineData("azuredevops://org/extra-path")]  // Should not have additional path segments
        [InlineData("azure://org")]  // Wrong scheme
        [InlineData("")]
        public void ResourceStateFactory_AzureDevOpsParallelJobsRegex_DoesNotMatchInvalidPatterns(string invalidId)
        {
            // Act
            var match = ResourceStateFactory.AzureDevOpsParallelJobs.Match(invalidId);

            // Assert
            Assert.False(match.Success);
        }

        [Theory]
        [InlineData("azuredevops://myorg")]
        [InlineData("azuredevops://my-org")]
        [InlineData("azuredevops://MyOrg123")]
        [InlineData("azuredevops://contoso")]
        public void ResourceStateFactory_AzureDevOpsParallelJobsRegex_MatchesVariousOrgNames(string validId)
        {
            // Act
            var match = ResourceStateFactory.AzureDevOpsParallelJobs.Match(validId);

            // Assert
            Assert.True(match.Success);
        }

        private AzureDevOpsParallelJobsResourceState CreateStateWithMockedClient()
        {
            var clientMock = new Mock<AzureDevOpsClient>(this.loggerMock.Object);
            return new AzureDevOpsParallelJobsResourceState(this.resourceId, this.loggerMock.Object, this.config, clientMock.Object);
        }
    }
}
