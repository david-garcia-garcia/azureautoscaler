using Azure.ResourceManager.Storage;
using Microsoft.Extensions.Logging;
using Moq;
using poolautoscaler.dimensions;
using poolautoscaler.resources;

namespace poolautoscaler.tests
{
    public class DimensionStorageFileShareThroughputTests
    {
        private readonly Mock<ILogger> _loggerMock;
        private readonly DimensionStorageFileShareThroughput _dimension;
        private readonly string _resourceId;

        public DimensionStorageFileShareThroughputTests()
        {
            _loggerMock = new Mock<ILogger>();
            _dimension = new DimensionStorageFileShareThroughput();
            _resourceId = "/subscriptions/test/resourceGroups/test/providers/Microsoft.Storage/storageAccounts/test/fileServices/default/shares/test";
        }

        [Fact]
        public void CanApplyDimension_WithFileShareResourceAndThroughputDimension_ShouldReturnTrue()
        {
            // Arrange
            var state = CreateStorageFileShareResourceState();
            var rule = new ScalingRule { Dimension = "Throughput" };

            // Act
            var result = _dimension.CanApplyDimension(state, rule, _loggerMock.Object);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void CanApplyDimension_WithFileShareResourceAndWrongDimension_ShouldReturnFalse()
        {
            // Arrange
            var state = CreateStorageFileShareResourceState();
            var rule = new ScalingRule { Dimension = "SomeOtherDimension" };

            // Act
            var result = _dimension.CanApplyDimension(state, rule, _loggerMock.Object);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void Compare_WithValidThroughputValues_ShouldReturnCorrectComparison()
        {
            // Arrange
            var mockResource = new Mock<FileShareResource>();

            // Act & Assert
            Assert.True(_dimension.Compare(mockResource.Object, "50", "100") < 0); // 50 < 100
            Assert.True(_dimension.Compare(mockResource.Object, "100", "50") > 0); // 100 > 50
            Assert.Equal(0, _dimension.Compare(mockResource.Object, "75", "75")); // 75 == 75
        }

        [Fact]
        public void Compare_WithInvalidValues_ShouldThrowArgumentException()
        {
            // Arrange
            var mockResource = new Mock<FileShareResource>();

            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() => 
                _dimension.Compare(mockResource.Object, "invalid", "100"));
            Assert.Contains("Invalid dimension values", exception.Message);
            Assert.Contains("Value1: 'invalid'", exception.Message);
            Assert.Contains("Value2: '100'", exception.Message);
        }

        [Fact]
        public void GetNextDimensionValue_ShouldIncreaseBy10Mbps()
        {
            // Arrange
            var state = CreateStorageFileShareResourceState();

            // Act
            var result = _dimension.GetNextDimensionValue(state, "80");

            // Assert
            Assert.Equal("90", result);
        }

        [Fact]
        public void GetPreviousDimensionValue_ShouldDecreaseBy10Mbps()
        {
            // Arrange
            var state = CreateStorageFileShareResourceState();

            // Act
            var result = _dimension.GetPreviousDimensionValue(state, "120");

            // Assert
            Assert.Equal("110", result);
        }

        [Fact]
        public void GetPreviousDimensionValue_WhenBelowMinimum_ShouldReturnMinimumThroughput()
        {
            // Arrange
            var state = CreateStorageFileShareResourceState();
            var minimumThroughput = StorageFileShareResourceStateHelper.GetThroughputFromQuota(100); // 110 MiB/s

            // Act
            var result = _dimension.GetPreviousDimensionValue(state, "10"); // Very low value

            // Assert
            Assert.Equal(minimumThroughput.ToString(), result); // Should return "110"
        }

        private StorageFileShareResourceState CreateStorageFileShareResourceState()
        {
            return new StorageFileShareResourceState(_resourceId, _loggerMock.Object, new Resource());
        }
    }
} 