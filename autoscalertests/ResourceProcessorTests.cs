using Azure.Core;
using Azure.ResourceManager;
using Microsoft.Extensions.Logging;
using Moq;
using poolautoscaler.configuration;
using poolautoscaler.dimensions;
using poolautoscaler.licensing;
using poolautoscaler.resourcemanagement;

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
