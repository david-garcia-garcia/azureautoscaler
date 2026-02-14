using Azure.Core;
using Azure.ResourceManager.Sql;
using Microsoft.Extensions.Logging;
using Moq;
using poolautoscaler.configuration;
using poolautoscaler.dimensions;
using poolautoscaler.resourcemanagement;
using poolautoscaler.resources.MssqlElasticPool;
using poolautoscaler.strategies;
using poolautoscaler.metrics.Dto;
using poolautoscaler.strategies.Dto;
using poolautoscaler.utils;

namespace poolautoscaler.tests
{
    public class RuleStrategyFixedTests
    {
        private readonly Mock<ILogger> loggerMock;
        private readonly RuleStrategyFixed strategy;
        private readonly DimensionAzureSqlElasticPoolDtu dimension;

        public RuleStrategyFixedTests()
        {
            this.loggerMock = new Mock<ILogger>();
            this.strategy = new RuleStrategyFixed();
            this.dimension = new DimensionAzureSqlElasticPoolDtu();
        }

        [Fact]
        public async Task EvaluateTargetDimensionValue_WithNoBounds_ShouldReturnCalculatedValue()
        {
            // Arrange
            var rule = this.CreateRule("(data) => (\"150\")");
            var metrics = new Dictionary<string, MetricEvalDtoResult>();
            var mockCredential = new Mock<TokenCredential>();
            var mockState = this.CreateMockResourceState();

            // Act
            var result = await this.strategy.EvaluateTargetDimensionValue(
                rule,
                this.dimension,
                mockState,
                this.loggerMock.Object,
                mockCredential.Object,
                CancellationToken.None,
                metrics
            );

            // Assert
            Assert.Equal("150", result);
        }

        [Fact]
        public async Task EvaluateTargetDimensionValue_WhenResultExceedsMax_ShouldReturnMax()
        {
            // Arrange
            var rule = this.CreateRule("(data) => (\"250\")", dimensionValueMax: "200");
            var metrics = new Dictionary<string, MetricEvalDtoResult>();
            var mockCredential = new Mock<TokenCredential>();
            var mockState = this.CreateMockResourceState();

            // Act
            var result = await this.strategy.EvaluateTargetDimensionValue(
                rule,
                this.dimension,
                mockState,
                this.loggerMock.Object,
                mockCredential.Object,
                CancellationToken.None,
                metrics
            );

            // Assert
            Assert.Equal("200", result);
        }

        [Fact]
        public async Task EvaluateTargetDimensionValue_WhenResultBelowMin_ShouldReturnMin()
        {
            // Arrange
            var rule = this.CreateRule("(data) => (\"30\")", dimensionValueMin: "50");
            var metrics = new Dictionary<string, MetricEvalDtoResult>();
            var mockCredential = new Mock<TokenCredential>();
            var mockState = this.CreateMockResourceState();

            // Act
            var result = await this.strategy.EvaluateTargetDimensionValue(
                rule,
                this.dimension,
                mockState,
                this.loggerMock.Object,
                mockCredential.Object,
                CancellationToken.None,
                metrics
            );

            // Assert
            Assert.Equal("50", result);
        }

        [Fact]
        public async Task EvaluateTargetDimensionValue_WhenResultWithinBounds_ShouldReturnCalculatedValue()
        {
            // Arrange
            var rule = this.CreateRule("(data) => (\"150\")", dimensionValueMax: "200", dimensionValueMin: "50");
            var metrics = new Dictionary<string, MetricEvalDtoResult>();
            var mockCredential = new Mock<TokenCredential>();
            var mockState = this.CreateMockResourceState();

            // Act
            var result = await this.strategy.EvaluateTargetDimensionValue(
                rule,
                this.dimension,
                mockState,
                this.loggerMock.Object,
                mockCredential.Object,
                CancellationToken.None,
                metrics
            );

            // Assert
            Assert.Equal("150", result);
        }

        [Fact]
        public async Task EvaluateTargetDimensionValue_WhenMaxOnlySetAndWithinBounds_ShouldReturnCalculatedValue()
        {
            // Arrange
            var rule = this.CreateRule("(data) => (\"150\")", dimensionValueMax: "200");
            var metrics = new Dictionary<string, MetricEvalDtoResult>();
            var mockCredential = new Mock<TokenCredential>();
            var mockState = this.CreateMockResourceState();

            // Act
            var result = await this.strategy.EvaluateTargetDimensionValue(
                rule,
                this.dimension,
                mockState,
                this.loggerMock.Object,
                mockCredential.Object,
                CancellationToken.None,
                metrics
            );

            // Assert
            Assert.Equal("150", result);
        }

        [Fact]
        public async Task EvaluateTargetDimensionValue_WhenMinOnlySetAndWithinBounds_ShouldReturnCalculatedValue()
        {
            // Arrange
            var rule = this.CreateRule("(data) => (\"150\")", dimensionValueMin: "50");
            var metrics = new Dictionary<string, MetricEvalDtoResult>();
            var mockCredential = new Mock<TokenCredential>();
            var mockState = this.CreateMockResourceState();

            // Act
            var result = await this.strategy.EvaluateTargetDimensionValue(
                rule,
                this.dimension,
                mockState,
                this.loggerMock.Object,
                mockCredential.Object,
                CancellationToken.None,
                metrics
            );

            // Assert
            Assert.Equal("150", result);
        }

        [Fact]
        public async Task EvaluateTargetDimensionValue_WithCeilingStep_ShouldRoundUpToNearestMultiple()
        {
            // Arrange
            var rule = this.CreateRule("(data) => (\"47\")", dimensionValueCeilingStep: "10");
            var metrics = new Dictionary<string, MetricEvalDtoResult>();
            var mockCredential = new Mock<TokenCredential>();
            var mockState = this.CreateMockResourceState();

            // Act
            var result = await this.strategy.EvaluateTargetDimensionValue(
                rule,
                this.dimension,
                mockState,
                this.loggerMock.Object,
                mockCredential.Object,
                CancellationToken.None,
                metrics
            );

            // Assert
            Assert.Equal("50", result);
        }

        [Fact]
        public async Task EvaluateTargetDimensionValue_WithCeilingStep_WhenValueIsAlreadyMultiple_ShouldNotRound()
        {
            // Arrange
            var rule = this.CreateRule("(data) => (\"50\")", dimensionValueCeilingStep: "10");
            var metrics = new Dictionary<string, MetricEvalDtoResult>();
            var mockCredential = new Mock<TokenCredential>();
            var mockState = this.CreateMockResourceState();

            // Act
            var result = await this.strategy.EvaluateTargetDimensionValue(
                rule,
                this.dimension,
                mockState,
                this.loggerMock.Object,
                mockCredential.Object,
                CancellationToken.None,
                metrics
            );

            // Assert
            Assert.Equal("50", result);
        }

        [Fact]
        public async Task EvaluateTargetDimensionValue_WithCeilingStep_WhenValueNeedsRounding_ShouldRoundUp()
        {
            // Arrange
            var rule = this.CreateRule("(data) => (\"51\")", dimensionValueCeilingStep: "10");
            var metrics = new Dictionary<string, MetricEvalDtoResult>();
            var mockCredential = new Mock<TokenCredential>();
            var mockState = this.CreateMockResourceState();

            // Act
            var result = await this.strategy.EvaluateTargetDimensionValue(
                rule,
                this.dimension,
                mockState,
                this.loggerMock.Object,
                mockCredential.Object,
                CancellationToken.None,
                metrics
            );

            // Assert
            Assert.Equal("60", result);
        }

        [Fact]
        public async Task EvaluateTargetDimensionValue_WithCeilingStep_WithDifferentStepSize_ShouldRoundCorrectly()
        {
            // Arrange
            var rule = this.CreateRule("(data) => (\"123\")", dimensionValueCeilingStep: "25");
            var metrics = new Dictionary<string, MetricEvalDtoResult>();
            var mockCredential = new Mock<TokenCredential>();
            var mockState = this.CreateMockResourceState();

            // Act
            var result = await this.strategy.EvaluateTargetDimensionValue(
                rule,
                this.dimension,
                mockState,
                this.loggerMock.Object,
                mockCredential.Object,
                CancellationToken.None,
                metrics
            );

            // Assert
            Assert.Equal("125", result);
        }

        [Fact]
        public async Task EvaluateTargetDimensionValue_WithCeilingStep_WithFloatingPointValue_ShouldRoundCorrectly()
        {
            // Arrange - Use a value that will result in a floating point after calculation
            // We'll use a calculation that results in 47.3, then apply ceiling step of 10
            var rule = this.CreateRule("(data) => ((47.3).ToString())", dimensionValueCeilingStep: "10");
            var metrics = new Dictionary<string, MetricEvalDtoResult>();
            var mockCredential = new Mock<TokenCredential>();
            var mockState = this.CreateMockResourceState();

            // Act
            var result = await this.strategy.EvaluateTargetDimensionValue(
                rule,
                this.dimension,
                mockState,
                this.loggerMock.Object,
                mockCredential.Object,
                CancellationToken.None,
                metrics
            );

            // Assert - Should round 47.3 up to 50 (nearest multiple of 10)
            // Parse as double to handle floating point formatting variations
            Assert.True(
                double.TryParse(result, out var parsedResult) && parsedResult == 50.0,
                $"Expected result to be 50, but got {result}");
        }

        [Fact]
        public async Task EvaluateTargetDimensionValue_WithCeilingStep_AndMaxBound_ShouldApplyCeilingThenMax()
        {
            // Arrange
            var rule = this.CreateRule("(data) => (\"47\")", dimensionValueMax: "45", dimensionValueCeilingStep: "10");
            var metrics = new Dictionary<string, MetricEvalDtoResult>();
            var mockCredential = new Mock<TokenCredential>();
            var mockState = this.CreateMockResourceState();

            // Act
            var result = await this.strategy.EvaluateTargetDimensionValue(
                rule,
                this.dimension,
                mockState,
                this.loggerMock.Object,
                mockCredential.Object,
                CancellationToken.None,
                metrics
            );

            // Assert - Ceiling rounds 47 to 50, but max is 45, so should return 45
            Assert.Equal("45", result);
        }

        [Fact]
        public async Task EvaluateTargetDimensionValue_WithCeilingStep_AndMinBound_ShouldApplyCeilingThenMin()
        {
            // Arrange
            var rule = this.CreateRule("(data) => (\"23\")", dimensionValueMin: "30", dimensionValueCeilingStep: "10");
            var metrics = new Dictionary<string, MetricEvalDtoResult>();
            var mockCredential = new Mock<TokenCredential>();
            var mockState = this.CreateMockResourceState();

            // Act
            var result = await this.strategy.EvaluateTargetDimensionValue(
                rule,
                this.dimension,
                mockState,
                this.loggerMock.Object,
                mockCredential.Object,
                CancellationToken.None,
                metrics
            );

            // Assert - Ceiling rounds 23 to 30, which matches min, so should return 30
            Assert.Equal("30", result);
        }

        [Fact]
        public async Task EvaluateTargetDimensionValue_WithCeilingStep_AndBothBounds_ShouldApplyAllConstraints()
        {
            // Arrange
            var rule = this.CreateRule("(data) => (\"47\")", dimensionValueMax: "200", dimensionValueMin: "50", dimensionValueCeilingStep: "10");
            var metrics = new Dictionary<string, MetricEvalDtoResult>();
            var mockCredential = new Mock<TokenCredential>();
            var mockState = this.CreateMockResourceState();

            // Act
            var result = await this.strategy.EvaluateTargetDimensionValue(
                rule,
                this.dimension,
                mockState,
                this.loggerMock.Object,
                mockCredential.Object,
                CancellationToken.None,
                metrics
            );

            // Assert - Ceiling rounds 47 to 50, which matches min, so should return 50
            Assert.Equal("50", result);
        }

        [Fact]
        public async Task EvaluateTargetDimensionValue_WithCeilingStep_WhenStepIsZero_ShouldNotApplyCeiling()
        {
            // Arrange
            var rule = this.CreateRule("(data) => (\"47\")", dimensionValueCeilingStep: "0");
            var metrics = new Dictionary<string, MetricEvalDtoResult>();
            var mockCredential = new Mock<TokenCredential>();
            var mockState = this.CreateMockResourceState();

            // Act
            var result = await this.strategy.EvaluateTargetDimensionValue(
                rule,
                this.dimension,
                mockState,
                this.loggerMock.Object,
                mockCredential.Object,
                CancellationToken.None,
                metrics
            );

            // Assert - Should return original value since step is 0
            Assert.Equal("47", result);
        }

        [Fact]
        public async Task EvaluateTargetDimensionValue_WithCeilingStep_WhenStepIsNegative_ShouldNotApplyCeiling()
        {
            // Arrange
            var rule = this.CreateRule("(data) => (\"47\")", dimensionValueCeilingStep: "-10");
            var metrics = new Dictionary<string, MetricEvalDtoResult>();
            var mockCredential = new Mock<TokenCredential>();
            var mockState = this.CreateMockResourceState();

            // Act
            var result = await this.strategy.EvaluateTargetDimensionValue(
                rule,
                this.dimension,
                mockState,
                this.loggerMock.Object,
                mockCredential.Object,
                CancellationToken.None,
                metrics
            );

            // Assert - Should return original value since step is negative
            Assert.Equal("47", result);
        }

        [Fact]
        public async Task EvaluateTargetDimensionValue_WithCeilingStep_WhenValueIsNotNumeric_ShouldReturnOriginalValue()
        {
            // Arrange
            var rule = this.CreateRule("(data) => (\"invalid\")", dimensionValueCeilingStep: "10");
            var metrics = new Dictionary<string, MetricEvalDtoResult>();
            var mockCredential = new Mock<TokenCredential>();
            var mockState = this.CreateMockResourceState();

            // Act
            var result = await this.strategy.EvaluateTargetDimensionValue(
                rule,
                this.dimension,
                mockState,
                this.loggerMock.Object,
                mockCredential.Object,
                CancellationToken.None,
                metrics
            );

            // Assert - Should return original value since it can't be parsed
            Assert.Equal("invalid", result);
        }

        [Fact]
        public async Task EvaluateTargetDimensionValue_WithCeilingStep_WhenStepIsNotNumeric_ShouldReturnOriginalValue()
        {
            // Arrange
            var rule = this.CreateRule("(data) => (\"47\")", dimensionValueCeilingStep: "invalid");
            var metrics = new Dictionary<string, MetricEvalDtoResult>();
            var mockCredential = new Mock<TokenCredential>();
            var mockState = this.CreateMockResourceState();

            // Act
            var result = await this.strategy.EvaluateTargetDimensionValue(
                rule,
                this.dimension,
                mockState,
                this.loggerMock.Object,
                mockCredential.Object,
                CancellationToken.None,
                metrics
            );

            // Assert - Should return original value since step can't be parsed
            Assert.Equal("47", result);
        }

        private ScalingRule CreateRule(string scaleTarget, string dimensionValueMax = null, string dimensionValueMin = null, string dimensionValueCeilingStep = null)
        {
            var rule = new ScalingRule
            {
                ScaleTarget = scaleTarget,
                DimensionValueMax = dimensionValueMax,
                DimensionValueMin = dimensionValueMin,
                DimensionValueCeilingStep = dimensionValueCeilingStep
            };

            // Parse the ScaleTarget expression
            if (!string.IsNullOrEmpty(rule.ScaleTarget))
            {
                rule.ScaleTargetExpression =
                    (Func<MetricEvalDto, string>)ExpressionParserUtils.ParseExpression(
                        rule.ScaleTarget,
                        "data",
                        typeof(MetricEvalDto),
                        typeof(string),
                        1);
            }

            return rule;
        }

        private MssqlElasticPoolResourceState CreateMockResourceState()
        {
            var resourceId = "/subscriptions/test/resourceGroups/test/providers/Microsoft.Sql/servers/test/elasticPools/test";
            var mockLogger = new Mock<ILogger>();
            var config = new Resource();

            var state = new MssqlElasticPoolResourceState(resourceId, mockLogger.Object, config);

            // Create a mock ElasticPoolResource - we just need something that implements ArmResource
            // The Compare method only uses string values, not the resource itself
            var mockResource = new Mock<ElasticPoolResource>();

            // Use reflection to set the base Resource field (it's public but the property accessor is read-only)
            var resourceField = typeof(ResourceState).GetField(
                "Resource",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (resourceField != null)
            {
                resourceField.SetValue(state, mockResource.Object);
            }

            return state;
        }
    }
}
