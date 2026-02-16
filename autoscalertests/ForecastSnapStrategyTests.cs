using poolautoscaler.configuration;
using poolautoscaler.metrics.Dto;
using poolautoscaler.metrics.ForecastModes;
using Xunit;

namespace poolautoscaler.tests
{
    /// <summary>Tests for forecast mode strategies (Anchors, AnchorWindow, Snap, Raw).</summary>
    public class ForecastSnapStrategyTests
    {
        [Fact]
        public void ForecastModeAnchor_TryApply_WithValidAnchorHours_FillsSnappedResult()
        {
            var strategy = new ForecastModeAnchor();
            var result = new MetricForecastResult
            {
                SlotMinutes = 60,
                ValueByDayAndHour = MakeBaseline(24, 50, 80),
                MetricId = "test"
            };
            var metric = new Metric
            {
                ForecastMode = "Anchors",
                ForecastSnapAnchorHours = new List<string> { "05:00", "20:00" },
                ForecastSnapPercentile = 90
            };

            var applied = strategy.TryApply(result, metric);

            Assert.True(applied);
            Assert.NotNull(result.SnappedValueByDayAndHour);
            Assert.Equal("Anchors", result.SnappedModeName);
            Assert.Equal(90, result.SnappedPercentile);
            Assert.NotNull(result.SnappedParameterSummary);
            Assert.Contains("05:00", result.SnappedParameterSummary);
            Assert.Contains("20:00", result.SnappedParameterSummary);
            Assert.True(result.SnappedValueByDayAndHour.TryGetValue(DayOfWeek.Monday, out var bySlot));
            Assert.True(bySlot.ContainsKey(0));
            Assert.True(bySlot.ContainsKey(5));
            Assert.True(bySlot.ContainsKey(20));
        }

        [Fact]
        public void ForecastModeAnchor_TryApply_WithNoAnchorHours_ReturnsFalse()
        {
            var strategy = new ForecastModeAnchor();
            var result = new MetricForecastResult { SlotMinutes = 60, ValueByDayAndHour = MakeBaseline(24), MetricId = "test" };
            var metric = new Metric { ForecastMode = "Anchors", ForecastSnapAnchorHours = new List<string>() };

            var applied = strategy.TryApply(result, metric);

            Assert.False(applied);
            Assert.Null(result.SnappedValueByDayAndHour);
        }

        [Fact]
        public void ForecastModeAnchorWindow_TryApply_WithValidWindows_FillsSnappedResult()
        {
            var strategy = new ForecastModeAnchorWindow();
            var result = new MetricForecastResult
            {
                SlotMinutes = 60,
                ValueByDayAndHour = MakeBaseline(24, 30, 70),
                MetricId = "test"
            };
            var metric = new Metric
            {
                ForecastMode = "AnchorWindow",
                ForecastAnchorWindows = new List<string> { "20:00-23:00", "03:00-07:00" },
                ForecastSnapPercentile = 80
            };

            var applied = strategy.TryApply(result, metric);

            Assert.True(applied);
            Assert.NotNull(result.SnappedValueByDayAndHour);
            Assert.Equal("AnchorWindow", result.SnappedModeName);
            Assert.Equal(80, result.SnappedPercentile);
            Assert.NotNull(result.SnappedParameterSummary);
            Assert.Contains("20:00-23:00", result.SnappedParameterSummary);
        }

        [Fact]
        public void ForecastModeAnchorWindow_TryApply_WithNoWindows_ReturnsFalse()
        {
            var strategy = new ForecastModeAnchorWindow();
            var result = new MetricForecastResult { SlotMinutes = 60, ValueByDayAndHour = MakeBaseline(24), MetricId = "test" };
            var metric = new Metric { ForecastMode = "AnchorWindow", ForecastAnchorWindows = new List<string>() };

            var applied = strategy.TryApply(result, metric);

            Assert.False(applied);
            Assert.Null(result.SnappedValueByDayAndHour);
        }

        [Fact]
        public void ForecastModeSnap_TryApply_WithValidConfig_FillsSnappedResult()
        {
            var strategy = new ForecastModeSnap();
            var result = new MetricForecastResult
            {
                SlotMinutes = 30,
                ValueByDayAndHour = MakeBaseline(48, 40, 60),
                MetricId = "test"
            };
            var metric = new Metric
            {
                ForecastMode = "Snap",
                ForecastSnapStepWindows = 3,
                ForecastSnapPercentile = 95
            };

            var applied = strategy.TryApply(result, metric);

            Assert.True(applied);
            Assert.NotNull(result.SnappedValueByDayAndHour);
            Assert.Equal("Snap", result.SnappedModeName);
            Assert.Equal(95, result.SnappedPercentile);
            Assert.Contains("3 windows", result.SnappedParameterSummary);
            Assert.True(result.SnappedValueByDayAndHour.TryGetValue(DayOfWeek.Monday, out var bySlot));
            Assert.True(bySlot.Count > 0);
        }

        [Fact]
        public void ForecastModeRaw_TryApply_ReturnsFalse()
        {
            var strategy = new ForecastModeRaw();
            var result = new MetricForecastResult { SlotMinutes = 60, ValueByDayAndHour = new Dictionary<DayOfWeek, Dictionary<int, double>>(), MetricId = "test" };
            var metric = new Metric { ForecastMode = "Raw" };

            var applied = strategy.TryApply(result, metric);

            Assert.False(applied);
            Assert.Null(result.SnappedValueByDayAndHour);
        }

        [Fact]
        public void ForecastModeHelper_PercentileValue_ReturnsCorrectPercentile()
        {
            var values = new List<double> { 10, 20, 30, 40, 50 };
            Assert.Equal(10, ForecastModeHelper.PercentileValue(values, 0));
            Assert.Equal(50, ForecastModeHelper.PercentileValue(values, 100));
            Assert.Equal(30, ForecastModeHelper.PercentileValue(values, 50));
            Assert.Equal(0, ForecastModeHelper.PercentileValue(new List<double>(), 50));
            Assert.Equal(25, ForecastModeHelper.PercentileValue(new List<double> { 25 }, 90));
        }

        [Fact]
        public void ForecastModeAnchor_ParseAnchorHoursToMinutes_ParsesCorrectly()
        {
            var parsed = ForecastModeAnchor.ParseAnchorHoursToMinutes(new List<string> { "05:00", "20:00" });
            Assert.NotNull(parsed);
            Assert.Equal(2, parsed.Count);
            Assert.Equal(300, parsed[0]);
            Assert.Equal(1200, parsed[1]);
        }

        private static Dictionary<DayOfWeek, Dictionary<int, double>> MakeBaseline(int slotsPerDay, double valueAtSlot0 = 10, double valueAtSlot1 = 20)
        {
            var days = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday };
            var d = new Dictionary<DayOfWeek, Dictionary<int, double>>();
            foreach (var day in days)
            {
                var bySlot = new Dictionary<int, double>();
                for (var s = 0; s < slotsPerDay; s++)
                {
                    bySlot[s] = s == 0 ? valueAtSlot0 : (s == 1 ? valueAtSlot1 : (10 + s));
                }

                d[day] = bySlot;
            }

            return d;
        }
    }
}
