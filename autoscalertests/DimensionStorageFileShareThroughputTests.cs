using Azure.ResourceManager.Storage;
using Microsoft.Extensions.Logging;
using Moq;
using poolautoscaler.configuration;
using poolautoscaler.dimensions;
using poolautoscaler.resources.StorageFileShare;

namespace poolautoscaler.tests
{
    public class DimensionStorageFileShareThroughputTests
    {
        private readonly Mock<ILogger> loggerMock;
        private readonly DimensionStorageFileShareThroughput dimension;
        private readonly string resourceId;

        public DimensionStorageFileShareThroughputTests()
        {
            this.loggerMock = new Mock<ILogger>();
            this.dimension = new DimensionStorageFileShareThroughput();
            this.resourceId = "/subscriptions/test/resourceGroups/test/providers/Microsoft.Storage/storageAccounts/test/fileServices/default/shares/test";
        }

        [Fact]
        public void CanApplyDimension_WithFileShareResourceAndThroughputDimension_ShouldReturnTrue()
        {
            // Arrange
            var state = this.CreateStorageFileShareResourceState();
            var rule = new ScalingRule { Dimension = "Throughput" };

            // Act
            var result = this.dimension.CanApplyDimension(state, rule, this.loggerMock.Object);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void CanApplyDimension_WithFileShareResourceAndWrongDimension_ShouldReturnFalse()
        {
            // Arrange
            var state = this.CreateStorageFileShareResourceState();
            var rule = new ScalingRule { Dimension = "SomeOtherDimension" };

            // Act
            var result = this.dimension.CanApplyDimension(state, rule, this.loggerMock.Object);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void Compare_WithValidThroughputValues_ShouldReturnCorrectComparison()
        {
            // Arrange
            var mockResource = new Mock<FileShareResource>();

            // Act & Assert
            Assert.True(this.dimension.Compare(mockResource.Object, "50", "100") < 0); // 50 < 100
            Assert.True(this.dimension.Compare(mockResource.Object, "100", "50") > 0); // 100 > 50
            Assert.Equal(0, this.dimension.Compare(mockResource.Object, "75", "75")); // 75 == 75
        }

        [Fact]
        public void Compare_WithInvalidValues_ShouldThrowArgumentException()
        {
            // Arrange
            var mockResource = new Mock<FileShareResource>();

            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() =>
                this.dimension.Compare(mockResource.Object, "invalid", "100"));
            Assert.Contains("Invalid dimension values", exception.Message);
            Assert.Contains("Value1: 'invalid'", exception.Message);
            Assert.Contains("Value2: '100'", exception.Message);
        }

        [Fact]
        public void GetNextDimensionValue_ShouldIncreaseBy10Mbps()
        {
            // Arrange
            var state = this.CreateStorageFileShareResourceState();

            // Act
            var result = this.dimension.GetNextDimensionValue(state, "80");

            // Assert
            Assert.Equal("90", result);
        }

        [Fact]
        public void GetPreviousDimensionValue_ShouldDecreaseBy10Mbps()
        {
            // Arrange
            var state = this.CreateStorageFileShareResourceState();

            // Act
            var result = this.dimension.GetPreviousDimensionValue(state, "120");

            // Assert
            Assert.Equal("110", result);
        }

        [Fact]
        public void GetPreviousDimensionValue_WhenBelowMinimum_ShouldReturnMinimumThroughput()
        {
            // Arrange
            var state = this.CreateStorageFileShareResourceState();
            var minimumThroughput = StorageFileShareResourceStateHelper.GetThroughputFromQuota(100); // 110 MiB/s

            // Act
            var result = this.dimension.GetPreviousDimensionValue(state, "10"); // Very low value

            // Assert
            Assert.Equal(minimumThroughput.ToString(), result); // Should return "110"
        }

        private StorageFileShareResourceState CreateStorageFileShareResourceState()
        {
            return new StorageFileShareResourceState(this.resourceId, this.loggerMock.Object, new Resource());
        }
    }
}
