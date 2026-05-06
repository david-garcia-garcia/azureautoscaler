using Microsoft.Extensions.Logging;
using Moq;
using poolautoscaler.configuration;

namespace poolautoscaler.tests
{
    public class ConfigFinderTests
    {
        private readonly ILogger logger = new Mock<ILogger>().Object;
        private readonly ConfigFinder finder = new();

        /// <summary>
        /// Absent YAML <c>TimeWindow:</c> yields a non-null default <see cref="TimeWindow"/>; the config is active at any instant within the default all-day UTC window.
        /// </summary>
        [Fact]
        public void ConfigFinder_WhenTimeWindowIsNull_ReturnsTrue()
        {
            var scalingConfig = CreateScalingConfigurationWithoutExplicitTimeWindow();
            var root = BuildConfigurationWithScaling(scalingConfig);
            root.PrepareAndValidate(this.logger);

            var utcTimes = new[]
            {
                new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 7, 15, 12, 30, 0, DateTimeKind.Utc),
                new DateTime(2026, 11, 30, 23, 58, 0, DateTimeKind.Utc),
            };

            foreach (var utc in utcTimes)
            {
                Assert.True(this.finder.SettingIsActive(scalingConfig, utc));
            }
        }

        [Fact]
        public void PrepareAndValidate_WhenTimeWindowIsAbsent_DoesNotThrow()
        {
            var scalingConfig = CreateScalingConfigurationWithoutExplicitTimeWindow();
            var root = BuildConfigurationWithScaling(scalingConfig);

            var ex = Record.Exception(() => root.PrepareAndValidate(this.logger));

            Assert.Null(ex);
        }

        /// <summary>
        /// Only <see cref="TimeWindow.Days"/> set; months and time-of-day use defaults (all months, 00:00–23:59 UTC).
        /// </summary>
        [Fact]
        public void ConfigFinder_WhenTimeWindowHasOnlyDays_MatchesAllTimesAndMonths()
        {
            var scalingConfig = new ScalingConfiguration
            {
                Id = "weekdays",
                TimeWindow = new TimeWindow { Days = "Weekday" },
                ScalingRules = new Dictionary<string, ScalingRule>
                {
                    ["fixed"] = new ScalingRule
                    {
                        Id = "fixed",
                        Dimension = "Dtu",
                        ScalingStrategy = "Fixed",
                        ScaleTarget = "(data) => (50).ToString()",
                    },
                },
            };

            var root = BuildConfigurationWithScaling(scalingConfig);
            root.PrepareAndValidate(this.logger);

            Assert.True(this.finder.SettingIsActive(scalingConfig, new DateTime(2026, 2, 10, 8, 15, 0, DateTimeKind.Utc)));
            Assert.True(this.finder.SettingIsActive(scalingConfig, new DateTime(2026, 6, 22, 21, 45, 0, DateTimeKind.Utc)));
            Assert.False(this.finder.SettingIsActive(scalingConfig, new DateTime(2026, 2, 14, 10, 0, 0, DateTimeKind.Utc)));
        }

        private static ScalingConfiguration CreateScalingConfigurationWithoutExplicitTimeWindow()
        {
            return new ScalingConfiguration
            {
                Id = "always-on",
                ScalingRules = new Dictionary<string, ScalingRule>
                {
                    ["fixed"] = new ScalingRule
                    {
                        Id = "fixed",
                        Dimension = "Dtu",
                        ScalingStrategy = "Fixed",
                        ScaleTarget = "(data) => (100).ToString()",
                    },
                },
            };
        }

        private static Configuration BuildConfigurationWithScaling(ScalingConfiguration scaling)
        {
            return new Configuration
            {
                DefaultResourceFrequency = "4m",
                ResourceDiscoveryFrequency = "1h",
                Resources =
                [
                    new Resource
                    {
                        Frequency = "4m",
                        ScalingConfigurations = new Dictionary<string, ScalingConfiguration> { [scaling.Id] = scaling },
                    },
                ],
            };
        }
    }
}
