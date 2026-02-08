using poolautoscaler.strategies;

namespace poolautoscaler.tests
{
    public class MetricEvalDtoResultValueTests
    {
        [Fact]
        public void GetAggregationType_WithAverage_ShouldReturnAvg()
        {
            // Arrange
            var value = new MetricEvalDtoResultValue { Average = 42.5 };

            // Act
            var result = value.GetAggregationType();

            // Assert
            Assert.Equal("avg", result);
        }

        [Fact]
        public void GetAggregationType_WithMaximum_ShouldReturnMax()
        {
            // Arrange
            var value = new MetricEvalDtoResultValue { Maximum = 100.0 };

            // Act
            var result = value.GetAggregationType();

            // Assert
            Assert.Equal("max", result);
        }

        [Fact]
        public void GetAggregationType_WithMinimum_ShouldReturnMin()
        {
            // Arrange
            var value = new MetricEvalDtoResultValue { Minimum = 5.0 };

            // Act
            var result = value.GetAggregationType();

            // Assert
            Assert.Equal("min", result);
        }

        [Fact]
        public void GetAggregationType_WithTotal_ShouldReturnTotal()
        {
            // Arrange
            var value = new MetricEvalDtoResultValue { Total = 1000.0 };

            // Act
            var result = value.GetAggregationType();

            // Assert
            Assert.Equal("total", result);
        }

        [Fact]
        public void GetAggregationType_WithCount_ShouldReturnCount()
        {
            // Arrange
            var value = new MetricEvalDtoResultValue { Count = 50.0 };

            // Act
            var result = value.GetAggregationType();

            // Assert
            Assert.Equal("count", result);
        }

        [Fact]
        public void GetAggregationType_WithCustomString_ShouldReturnCustom()
        {
            // Arrange
            var value = new MetricEvalDtoResultValue { CustomString = "Standard_D2ads_v5" };

            // Act
            var result = value.GetAggregationType();

            // Assert
            Assert.Equal("custom", result);
        }

        [Fact]
        public void GetAggregationType_WithNoValues_ShouldReturnUnknown()
        {
            // Arrange
            var value = new MetricEvalDtoResultValue();

            // Act
            var result = value.GetAggregationType();

            // Assert
            Assert.Equal("unknown", result);
        }

        [Fact]
        public void RenderValue_WithWholeNumber_ShouldRemoveDecimals()
        {
            // Arrange
            var value = new MetricEvalDtoResultValue { Average = 100.0 };

            // Act
            var result = value.RenderValue();

            // Assert
            Assert.Equal("100", result);
        }

        [Fact]
        public void RenderValue_WithDecimalNumber_ShouldShowTwoDecimals()
        {
            // Arrange
            var value = new MetricEvalDtoResultValue { Average = 42.567 };

            // Act
            var result = value.RenderValue();

            // Assert
            Assert.Equal("42.57", result);
        }

        [Fact]
        public void RenderValue_WithLargeWholeNumber_ShouldNotUseThousandSeparators()
        {
            // Arrange
            var value = new MetricEvalDtoResultValue { Average = 107374182400.0 };

            // Act
            var result = value.RenderValue();

            // Assert
            Assert.Equal("107374182400", result);
            Assert.DoesNotContain(",", result);
        }

        [Fact]
        public void RenderValue_WithCount_ShouldNotShowDecimals()
        {
            // Arrange
            var value = new MetricEvalDtoResultValue { Count = 42.0 };

            // Act
            var result = value.RenderValue();

            // Assert
            Assert.Equal("42", result);
        }

        [Fact]
        public void RenderValue_WithCustomString_ShouldReturnString()
        {
            // Arrange
            var value = new MetricEvalDtoResultValue { CustomString = "Standard_D2ads_v5" };

            // Act
            var result = value.RenderValue();

            // Assert
            Assert.Equal("Standard_D2ads_v5", result);
        }

        [Fact]
        public void RenderValue_WithNoValues_ShouldReturnNull()
        {
            // Arrange
            var value = new MetricEvalDtoResultValue();

            // Act
            var result = value.RenderValue();

            // Assert
            Assert.Equal("null", result);
        }

        [Fact]
        public void RenderValueWithStatus_WhenValid_ShouldReturnPlainValue()
        {
            // Arrange
            var value = new MetricEvalDtoResultValue 
            { 
                Average = 100.0,
                Valid = true
            };

            // Act
            var result = value.RenderValueWithStatus();

            // Assert
            Assert.Equal("100", result);
        }

        [Fact]
        public void RenderValueWithStatus_WhenInvalid_ShouldPrefixWithExclamation()
        {
            // Arrange
            var value = new MetricEvalDtoResultValue 
            { 
                Average = 0.0,
                Valid = false,
                InvalidReason = "Value is below minimum"
            };

            // Act
            var result = value.RenderValueWithStatus();

            // Assert
            Assert.Equal("!0", result);
            Assert.StartsWith("!", result);
        }

        [Fact]
        public void RenderValueWithStatus_WhenInvalidWithDecimal_ShouldPrefixCorrectly()
        {
            // Arrange
            var value = new MetricEvalDtoResultValue 
            { 
                Average = 42.50,
                Valid = false,
                InvalidReason = "Value is above maximum"
            };

            // Act
            var result = value.RenderValueWithStatus();

            // Assert
            Assert.Equal("!42.50", result);
        }

        [Fact]
        public void HasData_WithAverage_ShouldReturnTrue()
        {
            // Arrange
            var value = new MetricEvalDtoResultValue { Average = 42.0 };

            // Act
            var result = value.HasData();

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void HasData_WithCustomString_ShouldReturnTrue()
        {
            // Arrange
            var value = new MetricEvalDtoResultValue { CustomString = "test" };

            // Act
            var result = value.HasData();

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void HasData_WithNoValues_ShouldReturnFalse()
        {
            // Arrange
            var value = new MetricEvalDtoResultValue();

            // Act
            var result = value.HasData();

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void Valid_ShouldDefaultToTrue()
        {
            // Arrange & Act
            var value = new MetricEvalDtoResultValue();

            // Assert
            Assert.True(value.Valid);
        }

        [Fact]
        public void SetAverage_ShouldPopulateDefault()
        {
            // Arrange
            var value = new MetricEvalDtoResultValue();

            // Act
            value.SetAverage(42.5);

            // Assert
            Assert.Equal(42.5, value.Default);
            Assert.Equal(42.5, value.Average);
        }

        [Fact]
        public void SetMaximum_ShouldPopulateDefault()
        {
            // Arrange
            var value = new MetricEvalDtoResultValue();

            // Act
            value.SetMaximum(100.0);

            // Assert
            Assert.Equal(100.0, value.Default);
            Assert.Equal(100.0, value.Maximum);
        }

        [Fact]
        public void SetMinimum_ShouldPopulateDefault()
        {
            // Arrange
            var value = new MetricEvalDtoResultValue();

            // Act
            value.SetMinimum(5.0);

            // Assert
            Assert.Equal(5.0, value.Default);
            Assert.Equal(5.0, value.Minimum);
        }

        [Fact]
        public void SetTotal_ShouldPopulateDefault()
        {
            // Arrange
            var value = new MetricEvalDtoResultValue();

            // Act
            value.SetTotal(1000.0);

            // Assert
            Assert.Equal(1000.0, value.Default);
            Assert.Equal(1000.0, value.Total);
        }

        [Fact]
        public void SetCount_ShouldPopulateDefault()
        {
            // Arrange
            var value = new MetricEvalDtoResultValue();

            // Act
            value.SetCount(50.0);

            // Assert
            Assert.Equal(50.0, value.Default);
            Assert.Equal(50.0, value.Count);
        }

        [Fact]
        public void Default_WhenMultipleAggregationsSet_ShouldPrioritizeAverage()
        {
            // Arrange & Act
            var value = new MetricEvalDtoResultValue();
            value.SetMaximum(100.0);
            value.SetAverage(50.0);

            // Assert - Average takes priority
            Assert.Equal(50.0, value.Default);
        }
    }
}
