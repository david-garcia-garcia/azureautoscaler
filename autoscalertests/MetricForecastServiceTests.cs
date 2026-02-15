using Azure.Monitor.Query.Models;
using Microsoft.Extensions.Logging;
using Moq;
using poolautoscaler.configuration;
using poolautoscaler.metrics;
using poolautoscaler.metrics.Dto;

namespace poolautoscaler.tests
{
    /// <summary>Tests for <see cref="MetricForecastService"/> using pre-fetched history (ComputeForecastFromHistory).</summary>
    public class MetricForecastServiceTests
    {
        private readonly Mock<ILogger> loggerMock;
        private readonly MetricForecastService service;

        public MetricForecastServiceTests()
        {
            this.loggerMock = new Mock<ILogger>();
            this.service = new MetricForecastService(this.loggerMock.Object);
        }

        private static MetricEvalDtoResultValue Point(DateTimeOffset at, double value)
        {
            var v = new MetricEvalDtoResultValue
            {
                TimeStamp = at,
                Average = value,
                Maximum = value,
                Default = value
            };
            return v;
        }

        private static IList<MetricAggregationType> DefaultAggregations => new List<MetricAggregationType>
        {
            MetricAggregationType.Average,
            MetricAggregationType.Maximum
        };

        [Fact]
        public void ComputeForecastFromHistory_WithSingleDayData_ReturnsThatDayMaxAsForecast()
        {
            // One Monday: two points 10:00 and 11:00 UTC, values 50 and 80. Reference = Monday -> forecast = 80.
            var monday = new DateTimeOffset(2025, 2, 10, 0, 0, 0, TimeSpan.Zero); // Monday
            var main = new List<MetricEvalDtoResultValue>
            {
                Point(monday.AddHours(10), 50),
                Point(monday.AddHours(11), 80)
            };
            var max = new List<MetricEvalDtoResultValue>
            {
                Point(monday.AddHours(10), 100),
                Point(monday.AddHours(11), 100)
            };
            var start = monday;
            var end = monday.AddDays(1);

            var result = this.service.ComputeForecastFromHistory(
                main,
                max,
                start,
                end,
                monday.AddHours(12),
                DefaultAggregations,
                "test_metric");

            Assert.NotNull(result);
            Assert.True(result.Valid);
            Assert.Single(result.Values);
            Assert.Equal(80, result.Values[0].Default);
        }

        [Fact]
        public void ComputeForecastFromHistory_WithMultipleWeekdays_UsesAffinityAndReturnsMaxOfCompatible()
        {
            // Mon 40, Tue 60, Wed 90. Reference = Wednesday -> compatible = Wed (1.0) + Mon, Tue (0.3). Forecast = max(40,60,90) = 90.
            var wed = new DateTimeOffset(2025, 2, 12, 0, 0, 0, TimeSpan.Zero);
            var mon = wed.AddDays(-2);
            var tue = wed.AddDays(-1);
            var main = new List<MetricEvalDtoResultValue>
            {
                Point(mon.AddHours(10), 40),
                Point(tue.AddHours(10), 60),
                Point(wed.AddHours(10), 90)
            };
            var max = main.Select(p => Point(p.TimeStamp, 100)).ToList();
            var start = mon;
            var end = wed.AddDays(1);

            var result = this.service.ComputeForecastFromHistory(
                main,
                max,
                start,
                end,
                wed.AddHours(14),
                DefaultAggregations,
                "test_metric");

            Assert.NotNull(result);
            Assert.Equal(90, result.Values[0].Default);
        }

        [Fact]
        public void ComputeForecastFromHistory_WhenReferenceIsSaturday_OnlyWeekendWindowsIncluded()
        {
            // Sat 70, Sun 30. Reference = Saturday. Compatible = Sat (1.0) + Sun (0.3). Forecast = 70.
            var sat = new DateTimeOffset(2025, 2, 15, 0, 0, 0, TimeSpan.Zero);
            var sun = sat.AddDays(1);
            var main = new List<MetricEvalDtoResultValue>
            {
                Point(sat.AddHours(10), 70),
                Point(sun.AddHours(10), 30)
            };
            var max = main.Select(p => Point(p.TimeStamp, 100)).ToList();
            var start = sat;
            var end = sun.AddDays(1);

            var result = this.service.ComputeForecastFromHistory(
                main,
                max,
                start,
                end,
                sat.AddHours(12),
                DefaultAggregations,
                "test_metric");

            Assert.NotNull(result);
            Assert.Equal(70, result.Values[0].Default);
        }

        [Fact]
        public void ComputeForecastFromHistory_WithFullDay_IncludesAllPointsInDay()
        {
            // One day: points at 08:00 (20), 10:00 (60), 20:00 (100). Full day used -> max = 100.
            var day = new DateTimeOffset(2025, 2, 10, 0, 0, 0, TimeSpan.Zero);
            var main = new List<MetricEvalDtoResultValue>
            {
                Point(day.AddHours(8), 20),
                Point(day.AddHours(10), 60),
                Point(day.AddHours(20), 100)
            };
            var max = main.Select(p => Point(p.TimeStamp, 100)).ToList();
            var start = day;
            var end = day.AddDays(1);

            var result = this.service.ComputeForecastFromHistory(
                main,
                max,
                start,
                end,
                day.AddHours(12),
                DefaultAggregations,
                "test_metric");

            Assert.NotNull(result);
            Assert.Equal(100, result.Values[0].Default);
        }

        [Fact]
        public void ComputeForecastFromHistory_WhenUsageAtCap_FlagsAsInvalidAndCapped()
        {
            // One point at 98 with max 100 -> capped (>= 95%). Forecast value still 98 but Valid = false.
            var day = new DateTimeOffset(2025, 2, 10, 0, 0, 0, TimeSpan.Zero);
            var t = day.AddHours(10);
            var main = new List<MetricEvalDtoResultValue> { Point(t, 98) };
            var max = new List<MetricEvalDtoResultValue> { Point(t, 100) };
            var start = day;
            var end = day.AddDays(1);

            var result = this.service.ComputeForecastFromHistory(
                main,
                max,
                start,
                end,
                day.AddHours(12),
                DefaultAggregations,
                "test_metric");

            Assert.NotNull(result);
            Assert.False(result.Valid);
            Assert.Equal(98, result.Values[0].Default);
            Assert.Contains("underestimated", result.InvalidReason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ComputeForecastFromHistory_WhenUsageBelowCap_RemainsValid()
        {
            var day = new DateTimeOffset(2025, 2, 10, 0, 0, 0, TimeSpan.Zero);
            var t = day.AddHours(10);
            var main = new List<MetricEvalDtoResultValue> { Point(t, 80) };
            var max = new List<MetricEvalDtoResultValue> { Point(t, 100) };
            var start = day;
            var end = day.AddDays(1);

            var result = this.service.ComputeForecastFromHistory(
                main,
                max,
                start,
                end,
                day.AddHours(12),
                DefaultAggregations,
                "test_metric");

            Assert.NotNull(result);
            Assert.True(result.Valid);
            Assert.Equal(80, result.Values[0].Default);
        }

        [Fact]
        public void ComputeForecastFromHistory_WithEmptyMain_ReturnsInvalidResult()
        {
            var start = new DateTimeOffset(2025, 2, 10, 0, 0, 0, TimeSpan.Zero);
            var end = start.AddDays(3);
            var main = new List<MetricEvalDtoResultValue>();
            var max = new List<MetricEvalDtoResultValue>();

            var result = this.service.ComputeForecastFromHistory(
                main,
                max,
                start,
                end,
                start.AddHours(12),
                DefaultAggregations,
                "test_metric");

            Assert.NotNull(result);
            Assert.False(result.Valid);
            Assert.Empty(result.Values);
            Assert.Contains("No data", result.InvalidReason, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ComputeForecastFromHistory_WithMultipleDays_UsesAffinityForDayOfWeek()
        {
            // Data: Saturday 99, Monday 50. Reference Monday -> compatible = Monday (1.0). Forecast = 50.
            var sat = new DateTimeOffset(2025, 2, 15, 0, 0, 0, TimeSpan.Zero);
            var mon = sat.AddDays(2);
            var main = new List<MetricEvalDtoResultValue>
            {
                Point(sat.AddHours(10), 99),
                Point(mon.AddHours(10), 50)
            };
            var max = main.Select(p => Point(p.TimeStamp, 100)).ToList();
            var start = sat;
            var end = mon.AddDays(1);

            var result = this.service.ComputeForecastFromHistory(
                main,
                max,
                start,
                end,
                mon.AddHours(12),
                DefaultAggregations,
                "test_metric");

            Assert.NotNull(result);
            Assert.Equal(50, result.Values[0].Default);
        }

        [Fact]
        public void ComputeForecastFromHistory_AlignsMaxSeriesByTimestampWithinMargin()
        {
            // Main at 10:00, max at 10:12 (within 15 min). Both should be matched for cap check.
            var day = new DateTimeOffset(2025, 2, 10, 0, 0, 0, TimeSpan.Zero);
            var mainTs = day.AddHours(10);
            var maxTs = day.AddHours(10).AddMinutes(12);
            var main = new List<MetricEvalDtoResultValue> { Point(mainTs, 96) };
            var max = new List<MetricEvalDtoResultValue> { Point(maxTs, 100) };
            var start = day;
            var end = day.AddDays(1);

            var result = this.service.ComputeForecastFromHistory(
                main,
                max,
                start,
                end,
                day.AddHours(12),
                DefaultAggregations,
                "test_metric");

            Assert.NotNull(result);

            // 96 >= 0.95 * 100 -> capped
            Assert.False(result.Valid);
            Assert.Equal(96, result.Values[0].Default);
        }
    }
}
