using Azure;
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
        private readonly IArmClientWrapper armClientWrapper;
        private readonly LicenseInfo licenseInfo;
        private readonly IReadOnlyList<IDimension> dimensions;
        private readonly IResourceLocationResolver resourceLocationResolver;

        public ResourceProcessorTests()
        {
            this.logFactoryMock = new Mock<ILoggerFactory>();
            this.logger = new Mock<ILogger>().Object;
            this.logFactoryMock.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(this.logger);
            this.credentialMock = new Mock<TokenCredential>();
            this.armClientMock = new Mock<ArmClient>();
            this.armClientWrapper = new ArmClientWrapper(this.armClientMock.Object);
            this.licenseInfo = new LicenseInfo(new License { MaxResources = 10 }, isValid: true, isExpired: false);
            this.dimensions = Array.Empty<IDimension>();
            var resolverMock = new Mock<IResourceLocationResolver>();
            resolverMock.Setup(x => x.GetRegionAsync(It.IsAny<ArmClient>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string?)"westus2");
            this.resourceLocationResolver = resolverMock.Object;
        }

        [Fact]
        public async Task ProcessOneAsync_WhenTimeWindowExcludesCurrentTime_StillRefreshesAndExitsBeforeScaling()
        {
            // Arrange: TimeWindow 23:00-23:59 UTC (only last hour of day). We always refresh and push metrics first, then exit before scaling.
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
                this.armClientWrapper,
                this.licenseInfo,
                this.resourceLocationResolver,
                () => noonUtc);

            // Act
            await processor.ProcessOneAsync(state, CancellationToken.None);

            // Assert: state is refreshed (and metrics pushed if configured) before we skip scaling
            Assert.True(state.RefreshWasCalled);
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
                this.armClientWrapper,
                this.licenseInfo,
                this.resourceLocationResolver,
                () => noonUtc);

            // Act
            await processor.ProcessOneAsync(state, CancellationToken.None);

            // Assert
            Assert.True(state.RefreshWasCalled);
        }

        [Fact]
        public async Task ProcessOneAsync_WhenTimeWindowUsesWeekdayAndTodayIsWeekend_StillRefreshesAndExitsBeforeScaling()
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
                this.armClientWrapper,
                this.licenseInfo,
                this.resourceLocationResolver,
                () => saturdayNoon);

            // Act
            await processor.ProcessOneAsync(state, CancellationToken.None);

            // Assert: we still refresh (and push metrics if configured) before skipping scaling
            Assert.True(state.RefreshWasCalled);
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
                this.armClientWrapper,
                this.licenseInfo,
                this.resourceLocationResolver,
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
                this.armClientWrapper,
                this.licenseInfo,
                this.resourceLocationResolver,
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
                this.armClientWrapper,
                this.licenseInfo,
                this.resourceLocationResolver,
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
                this.armClientWrapper,
                this.licenseInfo,
                this.resourceLocationResolver,
                () => new DateTime(2025, 6, 16, 12, 0, 0, DateTimeKind.Utc));

            // Act
            await processor.ProcessOneAsync(state, CancellationToken.None);

            // Assert: Target = 10, same as current. RequestedCapacity is still set by SetDimensionValue (to 10).
            Assert.Equal(10, state.RequestedCapacity);
        }

        [Fact]
        public async Task ProcessOneAsync_WhenScaleDownWithinCooldown_SkipsScaleDown()
        {
            // Arrange: Metric triggers scale down (10 -> 5), but LastScale was 30s ago and ScaleDownCooldownSeconds = 60.
            var utcNow = new DateTime(2025, 6, 16, 12, 0, 0, DateTimeKind.Utc);
            var scalingConfig = CreateScalingConfigurationWithMetricAndRule(
                scaleTargetExpression: "(data) => (data.Metrics[\"cpu\"].Values.First().Default < 20 ? \"5\" : \"10\")",
                scaleDownCooldownSeconds: 60);
            var config = CreateResourceConfiguration(scalingConfig);
            var state = new TestResourceState("test://test", this.logger, config)
            {
                CurrentCapacity = 10,
                CustomMetricValues = { ["custom_test_cpu"] = 15 },
                LastScale = utcNow.AddSeconds(-30),
            };

            var dimensions = new List<IDimension> { new TestDimension() };
            var processor = new ResourceProcessor(
                this.logFactoryMock.Object,
                dimensions,
                this.credentialMock.Object,
                this.armClientWrapper,
                this.licenseInfo,
                this.resourceLocationResolver,
                () => utcNow);

            // Act
            await processor.ProcessOneAsync(state, CancellationToken.None);

            // Assert: Scale down skipped due to cooldown; SetDimensionValue never called, RequestedCapacity remains null.
            Assert.Null(state.RequestedCapacity);
        }

        [Fact]
        public async Task ProcessOneAsync_WhenScaleDownBeyondCooldown_AllowsScaleDown()
        {
            // Arrange: Metric triggers scale down (10 -> 5), LastScale was 2 min ago, ScaleDownCooldownSeconds = 60.
            var utcNow = new DateTime(2025, 6, 16, 12, 0, 0, DateTimeKind.Utc);
            var scalingConfig = CreateScalingConfigurationWithMetricAndRule(
                scaleTargetExpression: "(data) => (data.Metrics[\"cpu\"].Values.First().Default < 20 ? \"5\" : \"10\")",
                scaleDownCooldownSeconds: 60);
            var config = CreateResourceConfiguration(scalingConfig);
            var state = new TestResourceState("test://test", this.logger, config)
            {
                CurrentCapacity = 10,
                CustomMetricValues = { ["custom_test_cpu"] = 15 },
                LastScale = utcNow.AddSeconds(-120),
            };

            var dimensions = new List<IDimension> { new TestDimension() };
            var processor = new ResourceProcessor(
                this.logFactoryMock.Object,
                dimensions,
                this.credentialMock.Object,
                this.armClientWrapper,
                this.licenseInfo,
                this.resourceLocationResolver,
                () => utcNow);

            // Act
            await processor.ProcessOneAsync(state, CancellationToken.None);

            // Assert: Cooldown passed; scale down allowed, target 5 applied.
            Assert.Equal(5, state.RequestedCapacity);
        }

        [Fact]
        public async Task ProcessOneAsync_WhenScaleUpWithinCooldown_SkipsScaleUp()
        {
            // Arrange: Metric triggers scale up (10 -> 20), but LastScale was 30s ago and ScaleUpCooldownSeconds = 60.
            var utcNow = new DateTime(2025, 6, 16, 12, 0, 0, DateTimeKind.Utc);
            var scalingConfig = CreateScalingConfigurationWithMetricAndRule(
                scaleTargetExpression: "(data) => (data.Metrics[\"cpu\"].Values.First().Default > 80 ? \"20\" : \"10\")",
                scaleUpCooldownSeconds: 60);
            var config = CreateResourceConfiguration(scalingConfig);
            var state = new TestResourceState("test://test", this.logger, config)
            {
                CurrentCapacity = 10,
                CustomMetricValues = { ["custom_test_cpu"] = 90 },
                LastScale = utcNow.AddSeconds(-30),
            };

            var dimensions = new List<IDimension> { new TestDimension() };
            var processor = new ResourceProcessor(
                this.logFactoryMock.Object,
                dimensions,
                this.credentialMock.Object,
                this.armClientWrapper,
                this.licenseInfo,
                this.resourceLocationResolver,
                () => utcNow);

            // Act
            await processor.ProcessOneAsync(state, CancellationToken.None);

            // Assert: Scale up skipped due to cooldown; SetDimensionValue never called, RequestedCapacity remains null.
            Assert.Null(state.RequestedCapacity);
        }

        [Fact]
        public async Task ProcessOneAsync_WhenScaleUpBeyondCooldown_AllowsScaleUp()
        {
            // Arrange: Metric triggers scale up (10 -> 20), LastScale was 2 min ago, ScaleUpCooldownSeconds = 60.
            var utcNow = new DateTime(2025, 6, 16, 12, 0, 0, DateTimeKind.Utc);
            var scalingConfig = CreateScalingConfigurationWithMetricAndRule(
                scaleTargetExpression: "(data) => (data.Metrics[\"cpu\"].Values.First().Default > 80 ? \"20\" : \"10\")",
                scaleUpCooldownSeconds: 60);
            var config = CreateResourceConfiguration(scalingConfig);
            var state = new TestResourceState("test://test", this.logger, config)
            {
                CurrentCapacity = 10,
                CustomMetricValues = { ["custom_test_cpu"] = 90 },
                LastScale = utcNow.AddSeconds(-120),
            };

            var dimensions = new List<IDimension> { new TestDimension() };
            var processor = new ResourceProcessor(
                this.logFactoryMock.Object,
                dimensions,
                this.credentialMock.Object,
                this.armClientWrapper,
                this.licenseInfo,
                this.resourceLocationResolver,
                () => utcNow);

            // Act
            await processor.ProcessOneAsync(state, CancellationToken.None);

            // Assert: Cooldown passed; scale up allowed, target 20 applied.
            Assert.Equal(20, state.RequestedCapacity);
        }

        [Theory]
        [InlineData(0, false)]
        [InlineData(49, false)]
        [InlineData(50, true)]
        [InlineData(59, true)]
        public async Task ScaleDownLockWindowMinutes_BlocksFirstNMinutesOfHour(int minuteOfHour, bool expectScaleDownApplied)
        {
            var utcNow = new DateTime(2025, 6, 16, 12, minuteOfHour, 0, DateTimeKind.Utc);
            var scalingConfig = CreateScalingConfigurationWithMetricAndRule(
                scaleTargetExpression: "(data) => (data.Metrics[\"cpu\"].Values.First().Default < 20 ? \"5\" : \"10\")",
                scaleDownLockWindowMinutes: 50);
            var config = CreateResourceConfiguration(scalingConfig);
            var state = new TestResourceState("test://test", this.logger, config)
            {
                CurrentCapacity = 10,
                CustomMetricValues = { ["custom_test_cpu"] = 15 },
                LastScale = utcNow.AddMinutes(-10),
            };

            var dimensions = new List<IDimension> { new TestDimension() };
            var processor = new ResourceProcessor(
                this.logFactoryMock.Object,
                dimensions,
                this.credentialMock.Object,
                this.armClientWrapper,
                this.licenseInfo,
                this.resourceLocationResolver,
                () => utcNow);

            await processor.ProcessOneAsync(state, CancellationToken.None);

            if (expectScaleDownApplied)
            {
                Assert.Equal(5, state.RequestedCapacity);
            }
            else
            {
                Assert.Null(state.RequestedCapacity);
            }
        }

        [Theory]
        [InlineData(0, true)]
        [InlineData(57, true)]
        [InlineData(58, false)]
        [InlineData(59, false)]
        public async Task ScaleUpAllowWindowMinutes_BlocksFromMinuteNOnward(int minuteOfHour, bool expectScaleUpApplied)
        {
            var utcNow = new DateTime(2025, 6, 16, 12, minuteOfHour, 0, DateTimeKind.Utc);
            var scalingConfig = CreateScalingConfigurationWithMetricAndRule(
                scaleTargetExpression: "(data) => (data.Metrics[\"cpu\"].Values.First().Default > 80 ? \"20\" : \"10\")",
                scaleUpAllowWindowMinutes: 58);
            var config = CreateResourceConfiguration(scalingConfig);
            var state = new TestResourceState("test://test", this.logger, config)
            {
                CurrentCapacity = 10,
                CustomMetricValues = { ["custom_test_cpu"] = 90 },
                LastScale = utcNow.AddMinutes(-10),
            };

            var dimensions = new List<IDimension> { new TestDimension() };
            var processor = new ResourceProcessor(
                this.logFactoryMock.Object,
                dimensions,
                this.credentialMock.Object,
                this.armClientWrapper,
                this.licenseInfo,
                this.resourceLocationResolver,
                () => utcNow);

            await processor.ProcessOneAsync(state, CancellationToken.None);

            if (expectScaleUpApplied)
            {
                Assert.Equal(20, state.RequestedCapacity);
            }
            else
            {
                Assert.Null(state.RequestedCapacity);
            }
        }

        [Fact]
        public async Task ProcessOneAsync_WhenRequestFailedExceptionDuringEval_DisablesForOneHour()
        {
            var utcNow = new DateTime(2025, 6, 16, 12, 30, 0, DateTimeKind.Utc);
            var scalingConfig = CreateScalingConfigurationWithMetricAndRule(
                scaleTargetExpression: "(data) => (data.Metrics[\"cpu\"].Values.First().Default > 80 ? \"20\" : \"10\")");
            var config = CreateResourceConfiguration(scalingConfig);
            var state = new TestResourceState("test://test", this.logger, config)
            {
                CurrentCapacity = 10,
                CustomMetricValues = { ["custom_test_cpu"] = 90 },
                LastScale = utcNow.AddMinutes(-10),
            };

            var dimension = new TestDimension
            {
                ExceptionToThrowOnSet = new RequestFailedException(400, "pool busy", "ElasticPoolBusy", null),
            };
            var processor = new ResourceProcessor(
                this.logFactoryMock.Object,
                new List<IDimension> { dimension },
                this.credentialMock.Object,
                this.armClientWrapper,
                this.licenseInfo,
                this.resourceLocationResolver,
                () => utcNow);

            await processor.ProcessOneAsync(state, CancellationToken.None);

            // Transient Azure error codes are only handled inside MssqlElasticPoolResourceState.ApplyChanges.
            // A RequestFailedException thrown during the evaluation phase (e.g. from a dimension) is not
            // an elastic pool resource, so it falls through to the generic 1-hour disable.
            var key = state.DisabledUntil.Keys.Single(k => k.StartsWith(ResourceState.UnhandledExceptionPrefix, StringComparison.Ordinal));
            var until = state.DisabledUntil[key];
            Assert.InRange((until - DateTime.UtcNow).TotalHours, 0.95, 1.05);
        }

        [Fact]
        public async Task ProcessOneAsync_WhenRequestFailedException_UnknownCode_DisablesForOneHour()
        {
            var utcNow = new DateTime(2025, 6, 16, 12, 30, 0, DateTimeKind.Utc);
            var scalingConfig = CreateScalingConfigurationWithMetricAndRule(
                scaleTargetExpression: "(data) => (data.Metrics[\"cpu\"].Values.First().Default > 80 ? \"20\" : \"10\")");
            var config = CreateResourceConfiguration(scalingConfig);
            var state = new TestResourceState("test://test", this.logger, config)
            {
                CurrentCapacity = 10,
                CustomMetricValues = { ["custom_test_cpu"] = 90 },
                LastScale = utcNow.AddMinutes(-10),
            };

            var dimension = new TestDimension
            {
                ExceptionToThrowOnSet = new RequestFailedException(500, "nope", "TotallyUnknownCode", null),
            };
            var processor = new ResourceProcessor(
                this.logFactoryMock.Object,
                new List<IDimension> { dimension },
                this.credentialMock.Object,
                this.armClientWrapper,
                this.licenseInfo,
                this.resourceLocationResolver,
                () => utcNow);

            await processor.ProcessOneAsync(state, CancellationToken.None);

            var key = state.DisabledUntil.Keys.Single(k => k.StartsWith(ResourceState.UnhandledExceptionPrefix, StringComparison.Ordinal));
            var until = state.DisabledUntil[key];
            Assert.InRange((until - DateTime.UtcNow).TotalHours, 0.95, 1.05);
        }

        [Fact]
        public async Task ProcessOneAsync_WhenBackgroundApplyThrows_WithoutTransientDisable_DisablesForOneHour()
        {
            // Transient error handling lives in MssqlElasticPoolResourceState.ApplyChanges.
            // TestResourceState.ApplyChanges does not handle transient codes, so the processor
            // falls through to the generic 1-hour disable.
            var utcNow = new DateTime(2025, 6, 16, 12, 30, 0, DateTimeKind.Utc);
            var scalingConfig = CreateScalingConfigurationWithMetricAndRule(
                scaleTargetExpression: "(data) => (data.Metrics[\"cpu\"].Values.First().Default > 80 ? \"20\" : \"10\")");
            var config = CreateResourceConfiguration(scalingConfig);
            var state = new TestResourceState("test://test", this.logger, config)
            {
                CurrentCapacity = 10,
                CustomMetricValues = { ["custom_test_cpu"] = 90 },
                LastScale = utcNow.AddMinutes(-10),
                ApplyChangesException = new RequestFailedException(400, "catchup", "ElasticPoolUpdateLinksNotInCatchup", null),
            };

            var processor = new ResourceProcessor(
                this.logFactoryMock.Object,
                new List<IDimension> { new TestDimension() },
                this.credentialMock.Object,
                this.armClientWrapper,
                this.licenseInfo,
                this.resourceLocationResolver,
                () => utcNow);

            await processor.ProcessOneAsync(state, CancellationToken.None);

            await Task.Delay(500);

            var key = state.DisabledUntil.Keys.Single(k => k.StartsWith(ResourceState.UnhandledExceptionPrefix, StringComparison.Ordinal));
            var until = state.DisabledUntil[key];
            Assert.InRange((until - DateTime.UtcNow).TotalHours, 0.95, 1.05);
        }

        [Fact]
        public async Task ProcessOneAsync_WhenNoScalingConfigurations_StillRefreshesAndExitsBeforeScaling()
        {
            // Arrange: Empty ScalingConfigurations. We always refresh and push metrics first, then exit before scaling.
            var config = CreateResourceConfiguration(Array.Empty<ScalingConfiguration>());
            var state = new TestResourceState("test://test", this.logger, config);

            var processor = new ResourceProcessor(
                this.logFactoryMock.Object,
                this.dimensions,
                this.credentialMock.Object,
                this.armClientWrapper,
                this.licenseInfo,
                this.resourceLocationResolver,
                () => DateTime.UtcNow);

            // Act
            await processor.ProcessOneAsync(state, CancellationToken.None);

            // Assert: state is refreshed (and metrics pushed if configured) before we skip scaling
            Assert.True(state.RefreshWasCalled);
        }

        private static ScalingConfiguration CreateScalingConfigurationWithMetricAndRule(
            string scaleTargetExpression,
            int scaleUpCooldownSeconds = 0,
            int scaleDownCooldownSeconds = 0,
            int? scaleDownLockWindowMinutes = null,
            int? scaleUpAllowWindowMinutes = null)
        {
            var rule = new ScalingRule
            {
                Id = "rule1",
                Dimension = "TestCapacity",
                ScalingStrategy = "Fixed",
                ScaleTarget = scaleTargetExpression,
                ScaleUpCooldownSeconds = scaleUpCooldownSeconds,
                ScaleDownCooldownSeconds = scaleDownCooldownSeconds,
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

            var config = new ScalingConfiguration
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
            if (scaleDownLockWindowMinutes.HasValue)
            {
                config.ScaleDownLockWindowMinutes = scaleDownLockWindowMinutes;
            }

            if (scaleUpAllowWindowMinutes.HasValue)
            {
                config.ScaleUpAllowWindowMinutes = scaleUpAllowWindowMinutes;
            }

            return config;
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
