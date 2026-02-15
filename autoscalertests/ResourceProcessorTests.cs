using Azure.Core;
using Azure.ResourceManager;
using Microsoft.Extensions.Logging;
using Moq;
using poolautoscaler.configuration;
using poolautoscaler.dimensions;
using poolautoscaler.licensing;
using poolautoscaler.resourcemanagement;
using poolautoscaler.utils;

namespace poolautoscaler.tests
{
    public class ResourceProcessorTests
    {
        private readonly Mock<ILoggerFactory> logFactoryMock;
        private readonly ILogger logger;
        private readonly Mock<TokenCredential> credentialMock;
        private readonly Mock<ArmClient> armClientMock;
        private readonly LicenseInfo licenseInfo;
        private readonly IReadOnlyList<IDimension> dimensions;

        public ResourceProcessorTests()
        {
            this.logFactoryMock = new Mock<ILoggerFactory>();
            this.logger = new Mock<ILogger>().Object;
            this.logFactoryMock.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(this.logger);
            this.credentialMock = new Mock<TokenCredential>();
            this.armClientMock = new Mock<ArmClient>();
            this.licenseInfo = new LicenseInfo(new License { MaxResources = 10 }, isValid: true, isExpired: false);
            this.dimensions = Array.Empty<IDimension>();
        }

        [Fact]
        public async Task ProcessOneAsync_WhenTimeWindowExcludesCurrentTime_ExitsEarlyWithoutCallingRefresh()
        {
            // Arrange: TimeWindow 23:00-23:59 UTC (only last hour of day)
            var scalingConfig = CreateScalingConfiguration(
                id: "business-hours",
                startTime: TimeSpan.FromHours(23),
                endTime: new TimeSpan(23, 59, 0));
            var config = CreateResourceConfiguration(scalingConfig);
            var state = new TestResourceState("test://test", this.logger, config);

            var noonUtc = new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc);
            var processor = new ResourceProcessor(
                this.logFactoryMock.Object,
                this.dimensions,
                this.credentialMock.Object,
                this.armClientMock.Object,
                this.licenseInfo,
                () => noonUtc);

            // Act
            await processor.ProcessOneAsync(state, CancellationToken.None);

            // Assert
            Assert.False(state.RefreshWasCalled);
        }

        [Fact]
        public async Task ProcessOneAsync_WhenTimeWindowIncludesCurrentTime_CallsRefresh()
        {
            // Arrange: TimeWindow 00:00-23:59 UTC (all day)
            var scalingConfig = CreateScalingConfiguration(
                id: "all-day",
                startTime: TimeSpan.Zero,
                endTime: new TimeSpan(23, 59, 59));
            var config = CreateResourceConfiguration(scalingConfig);
            var state = new TestResourceState("test://test", this.logger, config);

            var noonUtc = new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc);
            var processor = new ResourceProcessor(
                this.logFactoryMock.Object,
                this.dimensions,
                this.credentialMock.Object,
                this.armClientMock.Object,
                this.licenseInfo,
                () => noonUtc);

            // Act
            await processor.ProcessOneAsync(state, CancellationToken.None);

            // Assert
            Assert.True(state.RefreshWasCalled);
        }

        [Fact]
        public async Task ProcessOneAsync_WhenTimeWindowUsesWeekdayAndTodayIsWeekend_ExitsEarlyWithoutRefresh()
        {
            // Arrange: Days = "Weekday" (Mon-Fri), use a Saturday
            var scalingConfig = CreateScalingConfiguration(
                id: "weekdays",
                startTime: TimeSpan.Zero,
                endTime: new TimeSpan(23, 59, 59),
                days: "Weekday");
            var config = CreateResourceConfiguration(scalingConfig);
            var state = new TestResourceState("test://test", this.logger, config);

            var saturdayNoon = new DateTime(2025, 6, 14, 12, 0, 0, DateTimeKind.Utc); // Saturday
            var processor = new ResourceProcessor(
                this.logFactoryMock.Object,
                this.dimensions,
                this.credentialMock.Object,
                this.armClientMock.Object,
                this.licenseInfo,
                () => saturdayNoon);

            // Act
            await processor.ProcessOneAsync(state, CancellationToken.None);

            // Assert
            Assert.False(state.RefreshWasCalled);
        }

        [Fact]
        public async Task ProcessOneAsync_WhenTimeWindowUsesWeekdayAndTodayIsWeekday_CallsRefresh()
        {
            // Arrange: Days = "Weekday", use a Monday
            var scalingConfig = CreateScalingConfiguration(
                id: "weekdays",
                startTime: TimeSpan.Zero,
                endTime: new TimeSpan(23, 59, 59),
                days: "Weekday");
            var config = CreateResourceConfiguration(scalingConfig);
            var state = new TestResourceState("test://test", this.logger, config);

            var mondayNoon = new DateTime(2025, 6, 16, 12, 0, 0, DateTimeKind.Utc); // Monday
            var processor = new ResourceProcessor(
                this.logFactoryMock.Object,
                this.dimensions,
                this.credentialMock.Object,
                this.armClientMock.Object,
                this.licenseInfo,
                () => mondayNoon);

            // Act
            await processor.ProcessOneAsync(state, CancellationToken.None);

            // Assert
            Assert.True(state.RefreshWasCalled);
        }

        [Fact]
        public async Task ProcessOneAsync_WhenMetricsIndicateScaleUp_EvaluatesLambdaAndScalesUp()
        {
            // Arrange: ScaleTarget expression returns 20 when cpu > 80, else 10. Metric value 90 triggers scale up.
            var scalingConfig = CreateScalingConfigurationWithMetricAndRule(
                scaleTargetExpression: "(data) => (data.Metrics[\"cpu\"].Values.First().Default > 80 ? \"20\" : \"10\")");
            var config = CreateResourceConfiguration(scalingConfig);
            var state = new TestResourceState("test://test", this.logger, config)
            {
                CurrentCapacity = 10,
                CustomMetricValues = { ["custom_test_cpu"] = 90 },
            };

            var dimensions = new List<IDimension> { new TestDimension() };
            var processor = new ResourceProcessor(
                this.logFactoryMock.Object,
                dimensions,
                this.credentialMock.Object,
                this.armClientMock.Object,
                this.licenseInfo,
                () => new DateTime(2025, 6, 16, 12, 0, 0, DateTimeKind.Utc));

            // Act
            await processor.ProcessOneAsync(state, CancellationToken.None);

            // Assert: Lambda evaluated cpu > 80 (90 > 80 = true), target = 20. Scale up from 10 to 20.
            Assert.Equal(20, state.RequestedCapacity);
        }

        [Fact]
        public async Task ProcessOneAsync_WhenMetricsIndicateScaleDown_EvaluatesLambdaAndScalesDown()
        {
            // Arrange: ScaleTarget expression returns 5 when cpu < 20, else 10. Metric value 15 triggers scale down.
            var scalingConfig = CreateScalingConfigurationWithMetricAndRule(
                scaleTargetExpression: "(data) => (data.Metrics[\"cpu\"].Values.First().Default < 20 ? \"5\" : \"10\")");
            var config = CreateResourceConfiguration(scalingConfig);
            var state = new TestResourceState("test://test", this.logger, config)
            {
                CurrentCapacity = 10,
                CustomMetricValues = { ["custom_test_cpu"] = 15 },
            };

            var dimensions = new List<IDimension> { new TestDimension() };
            var processor = new ResourceProcessor(
                this.logFactoryMock.Object,
                dimensions,
                this.credentialMock.Object,
                this.armClientMock.Object,
                this.licenseInfo,
                () => new DateTime(2025, 6, 16, 12, 0, 0, DateTimeKind.Utc));

            // Act
            await processor.ProcessOneAsync(state, CancellationToken.None);

            // Assert: Lambda evaluated cpu < 20 (15 < 20 = true), target = 5. Scale down from 10 to 5.
            Assert.Equal(5, state.RequestedCapacity);
        }

        [Fact]
        public async Task ProcessOneAsync_WhenMetricsIndicateNoChange_DoesNotScale()
        {
            // Arrange: ScaleTarget returns 10 when cpu between 20-80. Metric 50 = no change from current 10.
            var scalingConfig = CreateScalingConfigurationWithMetricAndRule(
                scaleTargetExpression: "(data) => (data.Metrics[\"cpu\"].Values.First().Default > 80 ? \"20\" : data.Metrics[\"cpu\"].Values.First().Default < 20 ? \"5\" : \"10\")");
            var config = CreateResourceConfiguration(scalingConfig);
            var state = new TestResourceState("test://test", this.logger, config)
            {
                CurrentCapacity = 10,
                CustomMetricValues = { ["custom_test_cpu"] = 50 },
            };

            var dimensions = new List<IDimension> { new TestDimension() };
            var processor = new ResourceProcessor(
                this.logFactoryMock.Object,
                dimensions,
                this.credentialMock.Object,
                this.armClientMock.Object,
                this.licenseInfo,
                () => new DateTime(2025, 6, 16, 12, 0, 0, DateTimeKind.Utc));

            // Act
            await processor.ProcessOneAsync(state, CancellationToken.None);

            // Assert: Target = 10, same as current. RequestedCapacity is still set by SetDimensionValue (to 10).
            Assert.Equal(10, state.RequestedCapacity);
        }

        [Fact]
        public async Task ProcessOneAsync_WhenNoScalingConfigurations_ExitsEarlyWithoutRefresh()
        {
            // Arrange: Empty ScalingConfigurations
            var config = CreateResourceConfiguration(Array.Empty<ScalingConfiguration>());
            var state = new TestResourceState("test://test", this.logger, config);

            var processor = new ResourceProcessor(
                this.logFactoryMock.Object,
                this.dimensions,
                this.credentialMock.Object,
                this.armClientMock.Object,
                this.licenseInfo,
                () => DateTime.UtcNow);

            // Act
            await processor.ProcessOneAsync(state, CancellationToken.None);

            // Assert
            Assert.False(state.RefreshWasCalled);
        }

        private static ScalingConfiguration CreateScalingConfigurationWithMetricAndRule(string scaleTargetExpression)
        {
            var rule = new ScalingRule
            {
                Id = "rule1",
                Dimension = "TestCapacity",
                ScalingStrategy = "Fixed",
                ScaleTarget = scaleTargetExpression,
            };
            rule.ScaleTargetExpression = (Func<poolautoscaler.strategies.Dto.MetricEvalDto, string>)ExpressionParserUtils.ParseExpression(
                rule.ScaleTarget,
                "data",
                typeof(poolautoscaler.strategies.Dto.MetricEvalDto),
                typeof(string),
                1);

            var metric = new Metric
            {
                Id = "cpu",
                Name = "custom_test_cpu",
                TransformExpression = a => a,
            };

            return new ScalingConfiguration
            {
                Id = "scale-by-cpu",
                TimeWindow = new TimeWindow
                {
                    TimeZone = "UTC",
                    Days = "All",
                    Months = "All",
                    StartTime = "00:00",
                    EndTime = "23:59",
                    StartTimeParsed = TimeSpan.Zero,
                    EndTimeParsed = new TimeSpan(23, 59, 59),
                },
                ScalingRules = new Dictionary<string, ScalingRule> { ["rule1"] = rule },
                Metrics = new Dictionary<string, Metric> { ["cpu"] = metric },
            };
        }

        private static ScalingConfiguration CreateScalingConfiguration(
            string id,
            TimeSpan startTime,
            TimeSpan endTime,
            string days = "All",
            string months = "All")
        {
            return new ScalingConfiguration
            {
                Id = id,
                TimeWindow = new TimeWindow
                {
                    TimeZone = "UTC",
                    Days = days,
                    Months = months,
                    StartTime = startTime.ToString(@"hh\:mm"),
                    EndTime = endTime.ToString(@"hh\:mm"),
                    StartTimeParsed = startTime,
                    EndTimeParsed = endTime,
                },
                ScalingRules = new Dictionary<string, ScalingRule>
                {
                    ["rule1"] = new ScalingRule { Id = "rule1", Dimension = "TestDimension" },
                },
                Metrics = new Dictionary<string, Metric>(),
            };
        }

        private static Resource CreateResourceConfiguration(params ScalingConfiguration[] scalingConfigs)
        {
            var configs = scalingConfigs.Length > 0
                ? scalingConfigs.ToDictionary(c => c.Id, c => c)
                : new Dictionary<string, ScalingConfiguration>();
            return new Resource
            {
                Enabled = true,
                FrequencyParsed = TimeSpan.FromMinutes(5),
                ScalingConfigurations = configs,
            };
        }
    }
}
