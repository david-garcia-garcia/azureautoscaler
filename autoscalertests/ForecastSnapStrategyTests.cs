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
        public void ForecastModeAnchorWindow_TryApply_OnlyChangesInsideConfiguredWindows()
        {
            var strategy = new ForecastModeAnchorWindow();
            var monday = new Dictionary<int, double>();
            for (var s = 0; s < 48; s++)
            {
                if (s < 6) // 00:00-03:00
                {
                    monday[s] = 85;
                }
                else if (s < 14) // 03:00-07:00
                {
                    monday[s] = 5;
                }
                else if (s < 40) // 07:00-20:00
                {
                    monday[s] = 11;
                }
                else if (s < 46) // 20:00-23:00
                {
                    monday[s] = 115;
                }
                else // 23:00-24:00
                {
                    monday[s] = 85;
                }
            }

            var result = new MetricForecastResult
            {
                SlotMinutes = 30,
                ValueByDayAndHour = new Dictionary<DayOfWeek, Dictionary<int, double>>
                {
                    [DayOfWeek.Monday] = monday
                },
                MetricId = "test"
            };

            var metric = new Metric
            {
                ForecastMode = "AnchorWindow",
                ForecastAnchorWindows = new List<string> { "20:00-23:00", "03:00-07:00" },
                ForecastSnapPercentile = 90
            };

            var applied = strategy.TryApply(result, metric);

            Assert.True(applied);
            Assert.NotNull(result.SnappedValueByDayAndHour);
            Assert.True(result.SnappedValueByDayAndHour.TryGetValue(DayOfWeek.Monday, out var bySlot));
            Assert.NotNull(bySlot);

            // Expect only two transitions for Monday:
            // 00:00-03:00 => 85, 03:00-20:00 => 5, 20:00-24:00 => 115.
            Assert.Equal(85, bySlot[0]);
            Assert.Equal(5, bySlot[6]);
            Assert.Equal(5, bySlot[39]);
            Assert.Equal(115, bySlot[40]);
            Assert.Equal(115, bySlot[47]);

            var ordered = bySlot.Keys.OrderBy(k => k).Select(k => bySlot[k]).ToList();
            var transitions = 0;
            for (var i = 1; i < ordered.Count; i++)
            {
                if (!ordered[i].Equals(ordered[i - 1]))
                {
                    transitions++;
                }
            }

            Assert.Equal(2, transitions);
        }

        [Fact]
        public void ForecastModeAnchorWindow_TryApply_CarriesValueAcrossMidnightWithoutWindowSwitch()
        {
            var strategy = new ForecastModeAnchorWindow();
            var monday = new Dictionary<int, double>();
            var tuesday = new Dictionary<int, double>();
            for (var s = 0; s < 48; s++)
            {
                // Monday window percentiles: 03:00-07:00 => 5, 20:00-23:00 => 115
                monday[s] = s < 6 ? 85 : (s < 14 ? 5 : (s < 40 ? 5 : (s < 46 ? 115 : 115)));

                // Tuesday day-start baseline differs on purpose; midnight should still carry Monday's final value.
                // Tuesday window percentiles: 03:00-07:00 => 6, 20:00-23:00 => 81
                tuesday[s] = s < 6 ? 94 : (s < 14 ? 6 : (s < 40 ? 6 : (s < 46 ? 81 : 81)));
            }

            var result = new MetricForecastResult
            {
                SlotMinutes = 30,
                ValueByDayAndHour = new Dictionary<DayOfWeek, Dictionary<int, double>>
                {
                    [DayOfWeek.Monday] = monday,
                    [DayOfWeek.Tuesday] = tuesday
                },
                MetricId = "test"
            };

            var metric = new Metric
            {
                ForecastMode = "AnchorWindow",
                ForecastAnchorWindows = new List<string> { "20:00-23:00", "03:00-07:00" },
                ForecastSnapPercentile = 90
            };

            var applied = strategy.TryApply(result, metric);

            Assert.True(applied);
            Assert.NotNull(result.SnappedValueByDayAndHour);
            Assert.True(result.SnappedValueByDayAndHour.TryGetValue(DayOfWeek.Monday, out var mon));
            Assert.True(result.SnappedValueByDayAndHour.TryGetValue(DayOfWeek.Tuesday, out var tue));

            // No switch window at midnight: Tuesday 00:00 should carry Monday 23:30.
            Assert.Equal(mon[47], tue[0]);
            Assert.Equal(115, tue[0]);
            Assert.Equal(115, tue[5]); // carry until 03:00 window starts
            Assert.Equal(6, tue[6]);   // switch at 03:00 window
        }

        [Fact]
        public void ForecastModeAnchorWindow_TryApply_CarriesValueFromSundayToMondayAtMidnight()
        {
            var strategy = new ForecastModeAnchorWindow();
            var sunday = new Dictionary<int, double>();
            var monday = new Dictionary<int, double>();
            for (var s = 0; s < 48; s++)
            {
                // Sunday window percentiles: 03:00-07:00 => 9, 20:00-23:00 => 31
                sunday[s] = s < 6 ? 20 : (s < 14 ? 9 : (s < 40 ? 9 : (s < 46 ? 31 : 31)));

                // Monday starts differently in baseline on purpose.
                monday[s] = s < 6 ? 85 : (s < 14 ? 5 : (s < 40 ? 5 : (s < 46 ? 115 : 115)));
            }

            var result = new MetricForecastResult
            {
                SlotMinutes = 30,
                ValueByDayAndHour = new Dictionary<DayOfWeek, Dictionary<int, double>>
                {
                    [DayOfWeek.Sunday] = sunday,
                    [DayOfWeek.Monday] = monday
                },
                MetricId = "test"
            };

            var metric = new Metric
            {
                ForecastMode = "AnchorWindow",
                ForecastAnchorWindows = new List<string> { "20:00-23:00", "03:00-07:00" },
                ForecastSnapPercentile = 90
            };

            var applied = strategy.TryApply(result, metric);

            Assert.True(applied);
            Assert.NotNull(result.SnappedValueByDayAndHour);
            Assert.True(result.SnappedValueByDayAndHour.TryGetValue(DayOfWeek.Sunday, out var sun));
            Assert.True(result.SnappedValueByDayAndHour.TryGetValue(DayOfWeek.Monday, out var mon));

            // No switch window at week boundary: Monday 00:00 should carry Sunday 23:30.
            Assert.Equal(sun[47], mon[0]);
            Assert.Equal(31, mon[0]);
            Assert.Equal(31, mon[5]); // carry until 03:00 window starts
            Assert.Equal(5, mon[6]);  // switch at 03:00 window
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
        public void ForecastModeSnap_TryApply_WithNullStepWindows_UsesDefaultThree()
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
                ForecastSnapStepWindows = null,
                ForecastSnapPercentile = 95
            };

            var applied = strategy.TryApply(result, metric);

            Assert.True(applied);
            Assert.Equal("Snap", result.SnappedModeName);
            Assert.Contains("3 windows", result.SnappedParameterSummary);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void ForecastModeSnap_TryApply_WithZeroOrNegativeStepWindows_ClampsToOneAndApplies(int stepWindows)
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
                ForecastSnapStepWindows = stepWindows,
                ForecastSnapPercentile = 95
            };

            var applied = strategy.TryApply(result, metric);

            Assert.True(applied);
            Assert.Equal("Snap", result.SnappedModeName);
            Assert.Contains("1 windows", result.SnappedParameterSummary);
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
