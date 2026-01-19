using Azure.Core;
using Azure.ResourceManager.Fabric;
using Microsoft.Extensions.Logging;
using Moq;
using poolautoscaler.dimensions;
using poolautoscaler.resources;

namespace poolautoscaler.tests
{
    public class DimensionFabricCapacitySkuTests
    {
        private readonly Mock<ILogger> _loggerMock;
        private readonly DimensionFabricCapacitySku _dimension;
        private readonly string _resourceId;

        public DimensionFabricCapacitySkuTests()
        {
            _loggerMock = new Mock<ILogger>();
            _dimension = new DimensionFabricCapacitySku();
            _resourceId = "/subscriptions/test/resourceGroups/test/providers/Microsoft.Fabric/capacities/test";
        }

        [Fact]
        public void CanApplyDimension_WithFabricCapacityResourceAndSkuDimension_ShouldReturnTrue()
        {
            // Arrange
            var state = CreateFabricCapacityResourceState();
            var rule = new ScalingRule { Dimension = "Sku" };

            // Act
            var result = _dimension.CanApplyDimension(state, rule, _loggerMock.Object);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void CanApplyDimension_WithFabricCapacityResourceAndWrongDimension_ShouldReturnFalse()
        {
            // Arrange
            var state = CreateFabricCapacityResourceState();
            var rule = new ScalingRule { Dimension = "SomeOtherDimension" };

            // Act
            var result = _dimension.CanApplyDimension(state, rule, _loggerMock.Object);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void CanApplyDimension_WithNonFabricCapacityResource_ShouldReturnFalse()
        {
            // Arrange
            var state = new MssqlElasticPoolResourceState(
                "/subscriptions/test/resourceGroups/test/providers/Microsoft.Sql/servers/test/elasticPools/test",
                _loggerMock.Object,
                new Resource());
            var rule = new ScalingRule { Dimension = "Sku" };

            // Act
            var result = _dimension.CanApplyDimension(state, rule, _loggerMock.Object);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void ValidateRuleConfiguration_WithValidSkuValues_ShouldNotThrow()
        {
            // Arrange
            var rule = new ScalingRule
            {
                Dimension = "Sku",
                DimensionValueMin = "F2",
                DimensionValueMax = "F8"
            };

            // Act & Assert
            _dimension.ValidateRuleConfiguration(rule); // Should not throw
        }

        [Fact]
        public void ValidateRuleConfiguration_WithInvalidMinSku_ShouldThrowArgumentException()
        {
            // Arrange
            var rule = new ScalingRule
            {
                Dimension = "Sku",
                DimensionValueMin = "InvalidSku"
            };

            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() => 
                _dimension.ValidateRuleConfiguration(rule));
            Assert.Contains("Invalid DimensionValueMin", exception.Message);
        }

        [Fact]
        public void ValidateRuleConfiguration_WithInvalidMaxSku_ShouldThrowArgumentException()
        {
            // Arrange
            var rule = new ScalingRule
            {
                Dimension = "Sku",
                DimensionValueMax = "InvalidSku"
            };

            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() => 
                _dimension.ValidateRuleConfiguration(rule));
            Assert.Contains("Invalid DimensionValueMax", exception.Message);
        }

        [Fact]
        public void Compare_WithValidSkuValues_ShouldReturnCorrectComparison()
        {
            // Arrange
            var mockResource = new Mock<FabricCapacityResource>();

            // Act & Assert
            Assert.True(_dimension.Compare(mockResource.Object, "F2", "F4") < 0); // F2 < F4
            Assert.True(_dimension.Compare(mockResource.Object, "F8", "F4") > 0); // F8 > F4
            Assert.Equal(0, _dimension.Compare(mockResource.Object, "F4", "F4")); // F4 == F4
        }

        [Fact]
        public void Compare_WithInvalidSkuValues_ShouldThrowArgumentException()
        {
            // Arrange
            var mockResource = new Mock<FabricCapacityResource>();

            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() => 
                _dimension.Compare(mockResource.Object, "InvalidSku", "F4"));
            Assert.Contains("Invalid SKU values", exception.Message);
        }

        [Fact]
        public void GetCurrentDimensionValue_ShouldReturnExistingSku()
        {
            // Arrange
            var state = CreateFabricCapacityResourceState();
            state.ExistingFabricCapacityState = new FabricCapacityResourceState.FabricCapacityState
            {
                Sku = "F4"
            };

            // Act
            var result = _dimension.GetCurrentDimensionValue(state);

            // Assert
            Assert.Equal("F4", result);
        }

        [Fact]
        public void GetCurrentDimensionValue_WhenSkuIsNull_ShouldReturnDefaultF2()
        {
            // Arrange
            var state = CreateFabricCapacityResourceState();
            state.ExistingFabricCapacityState = new FabricCapacityResourceState.FabricCapacityState
            {
                Sku = null
            };

            // Act
            var result = _dimension.GetCurrentDimensionValue(state);

            // Assert
            Assert.Equal("F2", result);
        }

        [Fact]
        public void GetNextDimensionValue_ShouldReturnNextSku()
        {
            // Arrange
            var state = CreateFabricCapacityResourceState();

            // Act
            var result = _dimension.GetNextDimensionValue(state, "F4");

            // Assert
            Assert.Equal("F8", result);
        }

        [Fact]
        public void GetNextDimensionValue_FromF2_ShouldReturnF4()
        {
            // Arrange
            var state = CreateFabricCapacityResourceState();

            // Act
            var result = _dimension.GetNextDimensionValue(state, "F2");

            // Assert
            Assert.Equal("F4", result); // F2 -> F4
        }

        [Fact]
        public void GetNextDimensionValue_WhenAtMax_ShouldReturnMaxSku()
        {
            // Arrange
            var state = CreateFabricCapacityResourceState();

            // Act
            var result = _dimension.GetNextDimensionValue(state, "F2048");

            // Assert
            Assert.Equal("F2048", result); // Already at max
        }

        [Fact]
        public void GetPreviousDimensionValue_ShouldReturnPreviousSku()
        {
            // Arrange
            var state = CreateFabricCapacityResourceState();

            // Act
            var result = _dimension.GetPreviousDimensionValue(state, "F8");

            // Assert
            Assert.Equal("F4", result);
        }

        [Fact]
        public void GetPreviousDimensionValue_FromF4_ShouldReturnF2()
        {
            // Arrange
            var state = CreateFabricCapacityResourceState();

            // Act
            var result = _dimension.GetPreviousDimensionValue(state, "F4");

            // Assert
            Assert.Equal("F2", result); // F4 -> F2
        }

        [Fact]
        public void GetPreviousDimensionValue_WhenAtMin_ShouldReturnMinSku()
        {
            // Arrange
            var state = CreateFabricCapacityResourceState();

            // Act
            var result = _dimension.GetPreviousDimensionValue(state, "F2");

            // Assert
            Assert.Equal("F2", result); // Already at min
        }

        [Fact]
        public void GetRequestedDimensionValue_ShouldReturnRequestedSku()
        {
            // Arrange
            var state = CreateFabricCapacityResourceState();
            state.RequestedFabricCapacityState = new FabricCapacityResourceState.FabricCapacityState
            {
                Sku = "F8"
            };

            // Act
            var result = _dimension.GetRequestedDimensionValue(state);

            // Assert
            Assert.Equal("F8", result);
        }

        [Fact]
        public void GetRequestedDimensionValue_WhenNoRequestedSku_ShouldReturnNull()
        {
            // Arrange
            var state = CreateFabricCapacityResourceState();
            state.RequestedFabricCapacityState = new FabricCapacityResourceState.FabricCapacityState
            {
                Sku = null
            };

            // Act
            var result = _dimension.GetRequestedDimensionValue(state);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task SetDimensionValue_WithValidSku_ShouldSetSku()
        {
            // Arrange
            var state = CreateFabricCapacityResourceState();
            state.RequestedFabricCapacityState = new FabricCapacityResourceState.FabricCapacityState();
            var mockCredential = new Mock<Azure.Core.TokenCredential>();
            var cancellationToken = CancellationToken.None;

            // Act
            await _dimension.SetDimensionValue(cancellationToken, state, _loggerMock.Object, mockCredential.Object, "F8");

            // Assert
            Assert.Equal("F8", state.RequestedFabricCapacityState.Sku);
        }

        [Fact]
        public async Task SetDimensionValue_WithInvalidSku_ShouldThrowArgumentException()
        {
            // Arrange
            var state = CreateFabricCapacityResourceState();
            var mockCredential = new Mock<Azure.Core.TokenCredential>();
            var cancellationToken = CancellationToken.None;

            // Act & Assert
            var exception = await Assert.ThrowsAsync<ArgumentException>(async () => 
                await _dimension.SetDimensionValue(cancellationToken, state, _loggerMock.Object, mockCredential.Object, "InvalidSku"));
            Assert.Contains("Invalid SKU value", exception.Message);
        }

        private FabricCapacityResourceState CreateFabricCapacityResourceState()
        {
            return new FabricCapacityResourceState(_resourceId, _loggerMock.Object, new Resource());
        }
    }
}
