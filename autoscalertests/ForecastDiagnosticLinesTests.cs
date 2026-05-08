using Azure.Monitor.Query.Models;
using Microsoft.Extensions.Logging;
using Moq;
using poolautoscaler.metrics;
using poolautoscaler.metrics.Dto;

namespace poolautoscaler.tests
{
    /// <summary>Tests for forecast diagnostic lines attached to <see cref="MetricEvalDtoResult"/>.</summary>
    public class ForecastDiagnosticLinesTests
    {
        private readonly Mock<ILogger> loggerMock = new Mock<ILogger>();
        private readonly MetricForecastService service;

        public ForecastDiagnosticLinesTests()
        {
            this.service = new MetricForecastService(this.loggerMock.Object);
        }

        [Fact]
        public void ComputeForecastFromHistory_ProducesDiagnosticLinesWithBaselineTable()
        {
            var monday = new DateTimeOffset(2025, 2, 10, 0, 0, 0, TimeSpan.Zero);
            var main = new List<MetricEvalDtoResultValue>
            {
                new MetricEvalDtoResultValue
                {
                    TimeStamp = monday.AddHours(10),
                    Average = 50,
                    Maximum = 50,
                    Default = 50
                },
                new MetricEvalDtoResultValue
                {
                    TimeStamp = monday.AddHours(11),
                    Average = 80,
                    Maximum = 80,
                    Default = 80
                }
            };
            var max = new List<MetricEvalDtoResultValue>
            {
                new MetricEvalDtoResultValue { TimeStamp = monday.AddHours(10), Average = 100, Maximum = 100, Default = 100 },
                new MetricEvalDtoResultValue { TimeStamp = monday.AddHours(11), Average = 100, Maximum = 100, Default = 100 }
            };
            var start = monday;
            var end = monday.AddDays(1);
            var aggregations = new List<MetricAggregationType> { MetricAggregationType.Average, MetricAggregationType.Maximum };

            var result = this.service.ComputeForecastFromHistory(
                main,
                max,
                start,
                end,
                monday.AddHours(12),
                aggregations,
                "m1");

            Assert.NotNull(result);
            Assert.NotNull(result!.Diagnostics);
            Assert.Contains(result.Diagnostics!.Lines, l => l.Contains("Full weekly forecast", StringComparison.Ordinal));
            Assert.Contains(result.Diagnostics.Lines, l => l.Contains("Reliability:", StringComparison.Ordinal));
            Assert.Contains(result.Diagnostics.Lines, l => l.Contains("m1", StringComparison.Ordinal));
            Assert.Contains(result.Diagnostics.Lines, l => l.Contains("Baseline aggregates", StringComparison.Ordinal));
            Assert.Contains(result.Diagnostics.Lines, l => l.Contains("Baseline aggregation mode", StringComparison.Ordinal) && l.Contains("Max", StringComparison.Ordinal));
            Assert.Contains(result.Diagnostics.TraceLines, l => l.Contains('|', StringComparison.Ordinal) && l.Contains('[', StringComparison.Ordinal));
        }

        [Fact]
        public void GetCurrentForecastValue_FromCachedResult_CopiesDiagnosticLinesToDto()
        {
            var wednesday = DayOfWeek.Wednesday;
            var fullForecast = new MetricForecastResult
            {
                MetricId = "cached_metric",
                SlotMinutes = 60,
                ValueByDayAndHour = new Dictionary<DayOfWeek, Dictionary<int, double>>
                {
                    [wednesday] = Enumerable.Range(0, 24).ToDictionary(i => i, i => (double)(100 + i))
                },
                CappedByDayAndHour = new Dictionary<DayOfWeek, Dictionary<int, bool>>(),
                ExecutedAggregations = new List<MetricAggregationType> { MetricAggregationType.Average },
                DiagnosticLines = new List<string> { "line-a", "line-b" }
            };

            var refTime = new DateTimeOffset(2025, 2, 12, 12, 0, 0, TimeSpan.Zero); // Wednesday noon UTC
            var dto = this.service.GetCurrentForecastValue(fullForecast, refTime);

            Assert.NotNull(dto.Diagnostics);
            Assert.Equal(2, dto.Diagnostics.Lines.Count);
            Assert.Equal("line-a", dto.Diagnostics.Lines[0]);
            Assert.Equal("line-b", dto.Diagnostics.Lines[1]);
        }

        [Fact]
        public void ForecastBuildInfo_AppendReliabilityDiagnosticLines_MatchesExpectedShape()
        {
            var start = new DateTimeOffset(2025, 2, 1, 0, 0, 0, TimeSpan.Zero);
            var end = new DateTimeOffset(2025, 2, 8, 0, 0, 0, TimeSpan.Zero);
            var samples = new Dictionary<DayOfWeek, int> { [DayOfWeek.Monday] = 3, [DayOfWeek.Tuesday] = 2 };
            var info = new ForecastBuildInfo(start, end, 5, samples);
            var lines = new List<string>();
            info.AppendReliabilityDiagnosticLines(lines);

            Assert.Equal(2, lines.Count);
            Assert.Contains("Reliability:", lines[0], StringComparison.Ordinal);
            Assert.Contains("2025-02-01", lines[0], StringComparison.Ordinal);
            Assert.Contains("2025-02-08", lines[0], StringComparison.Ordinal);
            Assert.Contains("5 days had data", lines[0], StringComparison.Ordinal);
            Assert.Contains("Samples per day of week:", lines[1], StringComparison.Ordinal);
            Assert.Contains("Monday: 3", lines[1], StringComparison.Ordinal);
        }

        [Fact]
        public void EmitDiagnostics_LogsEachLine()
        {
            var dto = new MetricEvalDtoResult
            {
                Valid = true,
                Diagnostics = new MetricEvaluationDiagnostics(new[] { "one", "two" })
            };
            dto.EmitDiagnostics(this.loggerMock.Object, LogLevel.Warning);
            this.loggerMock.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("one")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
            this.loggerMock.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("two")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public void EmitDiagnostics_EmitsTraceLinesAtTraceOnly()
        {
            var dto = new MetricEvalDtoResult
            {
                Valid = true,
                Diagnostics = new MetricEvaluationDiagnostics(new[] { "summary" }, new[] { "    Mon  11:00  36 | max | [26] | [26]" }),
            };
            dto.EmitDiagnostics(this.loggerMock.Object, LogLevel.Debug);
            this.loggerMock.Verify(
                x => x.Log(
                    LogLevel.Debug,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("summary")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
            this.loggerMock.Verify(
                x => x.Log(
                    LogLevel.Trace,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("36")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public void ComputeFullForecastFromHistory_Mean_AveragesContributorValues()
        {
            var mondayA = new DateTimeOffset(2025, 2, 10, 0, 0, 0, TimeSpan.Zero);
            var mondayB = new DateTimeOffset(2025, 2, 17, 0, 0, 0, TimeSpan.Zero);
            var start = mondayA;
            var end = new DateTimeOffset(2025, 2, 18, 12, 0, 0, TimeSpan.Zero);
            const int slot = 10; // 10:00 UTC with 60m slots

            var main = new List<MetricEvalDtoResultValue>
            {
                new MetricEvalDtoResultValue { TimeStamp = mondayA.AddHours(10), Average = 40, Maximum = 40, Default = 40 },
                new MetricEvalDtoResultValue { TimeStamp = mondayB.AddHours(10), Average = 60, Maximum = 60, Default = 60 },
            };
            var maxSeries = new List<MetricEvalDtoResultValue>
            {
                new MetricEvalDtoResultValue { TimeStamp = mondayA.AddHours(10), Average = 100, Maximum = 100, Default = 100 },
                new MetricEvalDtoResultValue { TimeStamp = mondayB.AddHours(10), Average = 100, Maximum = 100, Default = 100 },
            };

            var aggregations = new List<MetricAggregationType> { MetricAggregationType.Average, MetricAggregationType.Maximum };
            var forecast = this.service.ComputeFullForecastFromHistory(
                main,
                maxSeries,
                start,
                end,
                aggregations,
                "m_mean",
                slotMinutes: 60,
                baselineAggregation: "Mean");

            Assert.NotNull(forecast);
            Assert.NotNull(forecast!.DiagnosticLines);
            Assert.Contains(forecast.DiagnosticLines!, l => l.Contains("Baseline aggregation mode:", StringComparison.Ordinal) && l.Contains("Mean", StringComparison.Ordinal));
            Assert.True(forecast.ValueByDayAndHour.TryGetValue(DayOfWeek.Monday, out var bySlot));
            Assert.True(bySlot.TryGetValue(slot, out var v));
            Assert.Equal(50, v);
        }

        [Fact]
        public void ComputeFullForecastFromHistory_BaselineBoost_MultipliesAfterAggregation()
        {
            var mondayA = new DateTimeOffset(2025, 2, 10, 0, 0, 0, TimeSpan.Zero);
            var mondayB = new DateTimeOffset(2025, 2, 17, 0, 0, 0, TimeSpan.Zero);
            var start = mondayA;
            var end = new DateTimeOffset(2025, 2, 18, 12, 0, 0, TimeSpan.Zero);
            const int slot = 10;
            var main = new List<MetricEvalDtoResultValue>
            {
                new MetricEvalDtoResultValue { TimeStamp = mondayA.AddHours(10), Average = 40, Maximum = 40, Default = 40 },
                new MetricEvalDtoResultValue { TimeStamp = mondayB.AddHours(10), Average = 60, Maximum = 60, Default = 60 },
            };
            var maxSeries = new List<MetricEvalDtoResultValue>
            {
                new MetricEvalDtoResultValue { TimeStamp = mondayA.AddHours(10), Average = 100, Maximum = 100, Default = 100 },
                new MetricEvalDtoResultValue { TimeStamp = mondayB.AddHours(10), Average = 100, Maximum = 100, Default = 100 },
            };

            var aggregations = new List<MetricAggregationType> { MetricAggregationType.Average, MetricAggregationType.Maximum };
            var forecast = this.service.ComputeFullForecastFromHistory(
                main,
                maxSeries,
                start,
                end,
                aggregations,
                "m_boost",
                slotMinutes: 60,
                baselineAggregation: "Mean",
                baselineBoostFactor: 1.2);

            Assert.NotNull(forecast);
            Assert.Equal(1.2, forecast!.BaselineBoostFactorApplied, 10);
            Assert.True(forecast!.ValueByDayAndHour.TryGetValue(DayOfWeek.Monday, out var bySlot));
            Assert.True(bySlot.TryGetValue(slot, out var v));
            Assert.Equal(60, v);
            Assert.NotNull(forecast.DiagnosticLines);
            Assert.Contains(forecast.DiagnosticLines!, l =>
                l.Contains("Baseline boost:", StringComparison.Ordinal) &&
                l.Contains("x1.20", StringComparison.Ordinal) &&
                l.Contains("aggregation and smoothing", StringComparison.Ordinal));
        }

        [Fact]
        public void ComputeFullForecastFromHistory_WeightedMean_FavoursMoreRecentContributor()
        {
            var mondayOlder = new DateTimeOffset(2025, 2, 10, 0, 0, 0, TimeSpan.Zero);
            var mondayRecent = new DateTimeOffset(2025, 2, 17, 0, 0, 0, TimeSpan.Zero);
            var start = mondayOlder;
            var end = new DateTimeOffset(2025, 2, 18, 12, 0, 0, TimeSpan.Zero);
            const int slot = 10;
            var main = new List<MetricEvalDtoResultValue>
            {
                new MetricEvalDtoResultValue { TimeStamp = mondayOlder.AddHours(10), Average = 20, Maximum = 20, Default = 20 },
                new MetricEvalDtoResultValue { TimeStamp = mondayRecent.AddHours(10), Average = 80, Maximum = 80, Default = 80 },
            };
            var maxSeries = new List<MetricEvalDtoResultValue>
            {
                new MetricEvalDtoResultValue { TimeStamp = mondayOlder.AddHours(10), Average = 100, Maximum = 100, Default = 100 },
                new MetricEvalDtoResultValue { TimeStamp = mondayRecent.AddHours(10), Average = 100, Maximum = 100, Default = 100 },
            };

            var aggregations = new List<MetricAggregationType> { MetricAggregationType.Average, MetricAggregationType.Maximum };
            var forecast = this.service.ComputeFullForecastFromHistory(
                main,
                maxSeries,
                start,
                end,
                aggregations,
                "m_wavg",
                slotMinutes: 60,
                baselineAggregation: "WeightedMean",
                baselineDecayDays: 7);

            Assert.NotNull(forecast);
            Assert.True(forecast!.ValueByDayAndHour.TryGetValue(DayOfWeek.Monday, out var bySlot));
            Assert.True(bySlot.TryGetValue(slot, out var v));

            Assert.True(v > 50, $"expected weighted mean above arithmetic midpoint 50; got {v}");
            Assert.True(v < 80, $"expected below the recent contributor 80; got {v}");
        }

        [Fact]
        public void ComputeFullForecastFromHistory_SingleContributor_SameAcrossModes()
        {
            var monday = new DateTimeOffset(2025, 2, 10, 0, 0, 0, TimeSpan.Zero);
            var start = monday;
            var end = monday.AddDays(1);
            const int slot = 11;
            const double expected = 55;
            var main = new List<MetricEvalDtoResultValue>
            {
                new MetricEvalDtoResultValue { TimeStamp = monday.AddHours(11), Average = expected, Maximum = expected, Default = expected },
            };
            var maxSeries = new List<MetricEvalDtoResultValue>
            {
                new MetricEvalDtoResultValue { TimeStamp = monday.AddHours(11), Average = 100, Maximum = 100, Default = 100 },
            };

            var aggregations = new List<MetricAggregationType> { MetricAggregationType.Average, MetricAggregationType.Maximum };
            foreach (var mode in new[] { (string?)null, "Max", "Mean", "WeightedMean" })
            {
                var forecast = this.service.ComputeFullForecastFromHistory(
                    main,
                    maxSeries,
                    start,
                    end,
                    aggregations,
                    "single",
                    slotMinutes: 60,
                    baselineAggregation: mode);
                Assert.NotNull(forecast);
                Assert.True(forecast!.ValueByDayAndHour.TryGetValue(DayOfWeek.Monday, out var bySlot));
                Assert.True(bySlot.TryGetValue(slot, out var v));
                Assert.Equal(expected, v);
            }
        }

        [Fact]
        public void ComputeFullForecastFromHistory_WeightedMean_NonPositiveDecayUsesDefaultHalfLife()
        {
            var mondayOlder = new DateTimeOffset(2025, 2, 10, 0, 0, 0, TimeSpan.Zero);
            var mondayRecent = new DateTimeOffset(2025, 2, 17, 0, 0, 0, TimeSpan.Zero);
            var start = mondayOlder;
            var end = new DateTimeOffset(2025, 2, 18, 12, 0, 0, TimeSpan.Zero);
            const int slot = 10;
            var main = new List<MetricEvalDtoResultValue>
            {
                new MetricEvalDtoResultValue { TimeStamp = mondayOlder.AddHours(10), Average = 10, Maximum = 10, Default = 10 },
                new MetricEvalDtoResultValue { TimeStamp = mondayRecent.AddHours(10), Average = 90, Maximum = 90, Default = 90 },
            };
            var maxSeries = new List<MetricEvalDtoResultValue>
            {
                new MetricEvalDtoResultValue { TimeStamp = mondayOlder.AddHours(10), Average = 100, Maximum = 100, Default = 100 },
                new MetricEvalDtoResultValue { TimeStamp = mondayRecent.AddHours(10), Average = 100, Maximum = 100, Default = 100 },
            };

            var aggregations = new List<MetricAggregationType> { MetricAggregationType.Average, MetricAggregationType.Maximum };
            foreach (var badDecay in new double?[] { 0, -1 })
            {
                var forecast = this.service.ComputeFullForecastFromHistory(
                    main,
                    maxSeries,
                    start,
                    end,
                    aggregations,
                    "m_decay",
                    slotMinutes: 60,
                    baselineAggregation: "WeightedMean",
                    baselineDecayDays: badDecay);

                Assert.NotNull(forecast);
                Assert.True(forecast!.ValueByDayAndHour.TryGetValue(DayOfWeek.Monday, out var bySlot));
                Assert.True(bySlot.TryGetValue(slot, out var v));
                Assert.False(double.IsNaN(v));
                Assert.InRange(v, 10, 90);
            }
        }

        [Fact]
        public void ComputeFullForecastFromHistory_UnsetBaselineAggregation_MatchesExplicitMax()
        {
            var mondayA = new DateTimeOffset(2025, 2, 10, 0, 0, 0, TimeSpan.Zero);
            var mondayB = new DateTimeOffset(2025, 2, 17, 0, 0, 0, TimeSpan.Zero);
            var start = mondayA;
            var end = new DateTimeOffset(2025, 2, 18, 12, 0, 0, TimeSpan.Zero);
            const int slot = 14;
            var main = new List<MetricEvalDtoResultValue>
            {
                new MetricEvalDtoResultValue { TimeStamp = mondayA.AddHours(14), Average = 30, Maximum = 30, Default = 30 },
                new MetricEvalDtoResultValue { TimeStamp = mondayB.AddHours(14), Average = 95, Maximum = 95, Default = 95 },
            };
            var maxSeries = new List<MetricEvalDtoResultValue>
            {
                new MetricEvalDtoResultValue { TimeStamp = mondayA.AddHours(14), Average = 150, Maximum = 150, Default = 150 },
                new MetricEvalDtoResultValue { TimeStamp = mondayB.AddHours(14), Average = 150, Maximum = 150, Default = 150 },
            };

            var aggregations = new List<MetricAggregationType> { MetricAggregationType.Average, MetricAggregationType.Maximum };
            var unset = this.service.ComputeFullForecastFromHistory(
                main,
                maxSeries,
                start,
                end,
                aggregations,
                "m_unset",
                slotMinutes: 60,
                baselineAggregation: null);
            var explicitMax = this.service.ComputeFullForecastFromHistory(
                main,
                maxSeries,
                start,
                end,
                aggregations,
                "m_max",
                slotMinutes: 60,
                baselineAggregation: "Max");

            Assert.NotNull(unset);
            Assert.NotNull(explicitMax);
            Assert.True(unset!.ValueByDayAndHour[DayOfWeek.Monday].TryGetValue(slot, out var u));
            Assert.True(explicitMax!.ValueByDayAndHour[DayOfWeek.Monday].TryGetValue(slot, out var m));
            Assert.Equal(95, m);
            Assert.Equal(m, u);
        }

        [Fact]
        public void ComputeFullForecastFromHistory_TemporalSmoothing_LiftsDipBetweenHighNeighbours()
        {
            var mondayA = new DateTimeOffset(2025, 2, 10, 0, 0, 0, TimeSpan.Zero);
            var mondayB = new DateTimeOffset(2025, 2, 17, 0, 0, 0, TimeSpan.Zero);
            var start = mondayA;
            var end = new DateTimeOffset(2025, 2, 18, 12, 0, 0, TimeSpan.Zero);
            const int dipSlot = 10;
            var main = new List<MetricEvalDtoResultValue>
            {
                new MetricEvalDtoResultValue { TimeStamp = mondayA.AddHours(9), Average = 100, Maximum = 100, Default = 100 },
                new MetricEvalDtoResultValue { TimeStamp = mondayA.AddHours(10), Average = 50, Maximum = 50, Default = 50 },
                new MetricEvalDtoResultValue { TimeStamp = mondayA.AddHours(11), Average = 100, Maximum = 100, Default = 100 },
                new MetricEvalDtoResultValue { TimeStamp = mondayB.AddHours(9), Average = 100, Maximum = 100, Default = 100 },
                new MetricEvalDtoResultValue { TimeStamp = mondayB.AddHours(10), Average = 50, Maximum = 50, Default = 50 },
                new MetricEvalDtoResultValue { TimeStamp = mondayB.AddHours(11), Average = 100, Maximum = 100, Default = 100 },
            };
            var maxSeries = main.Select(m => new MetricEvalDtoResultValue
            {
                TimeStamp = m.TimeStamp,
                Average = 200,
                Maximum = 200,
                Default = 200,
            }).ToList();
            var aggregations = new List<MetricAggregationType> { MetricAggregationType.Average, MetricAggregationType.Maximum };

            var forecast = this.service.ComputeFullForecastFromHistory(
                main,
                maxSeries,
                start,
                end,
                aggregations,
                "m_smooth_dip",
                slotMinutes: 60,
                baselineAggregation: "Mean",
                baselineSmoothingNeighbourWeight: 0.15);

            Assert.NotNull(forecast);
            Assert.Equal(0.15, forecast!.BaselineSmoothingNeighbourWeightApplied, 10);
            Assert.True(forecast.ValueByDayAndHour.TryGetValue(DayOfWeek.Monday, out var bySlot));
            Assert.True(bySlot.TryGetValue(dipSlot, out var v));
            Assert.Equal(65, v, 10);
            Assert.Contains(forecast.DiagnosticLines!, l =>
                l.Contains("Temporal smoothing neighbour weight", StringComparison.Ordinal) &&
                l.Contains("0.15", StringComparison.Ordinal));
        }

        [Fact]
        public void ComputeFullForecastFromHistory_TemporalSmoothing_DoesNotLowerLocalPeak()
        {
            var mondayA = new DateTimeOffset(2025, 2, 10, 0, 0, 0, TimeSpan.Zero);
            var mondayB = new DateTimeOffset(2025, 2, 17, 0, 0, 0, TimeSpan.Zero);
            var start = mondayA;
            var end = new DateTimeOffset(2025, 2, 18, 12, 0, 0, TimeSpan.Zero);
            const int peakSlot = 15;
            var main = new List<MetricEvalDtoResultValue>
            {
                new MetricEvalDtoResultValue { TimeStamp = mondayA.AddHours(14), Average = 50, Maximum = 50, Default = 50 },
                new MetricEvalDtoResultValue { TimeStamp = mondayA.AddHours(15), Average = 90, Maximum = 90, Default = 90 },
                new MetricEvalDtoResultValue { TimeStamp = mondayA.AddHours(16), Average = 70, Maximum = 70, Default = 70 },
                new MetricEvalDtoResultValue { TimeStamp = mondayB.AddHours(14), Average = 50, Maximum = 50, Default = 50 },
                new MetricEvalDtoResultValue { TimeStamp = mondayB.AddHours(15), Average = 90, Maximum = 90, Default = 90 },
                new MetricEvalDtoResultValue { TimeStamp = mondayB.AddHours(16), Average = 70, Maximum = 70, Default = 70 },
            };
            var maxSeries = main.Select(m => new MetricEvalDtoResultValue { TimeStamp = m.TimeStamp, Average = 200, Maximum = 200, Default = 200 }).ToList();
            var aggregations = new List<MetricAggregationType> { MetricAggregationType.Average, MetricAggregationType.Maximum };

            var forecast = this.service.ComputeFullForecastFromHistory(
                main,
                maxSeries,
                start,
                end,
                aggregations,
                "m_smooth_peak",
                slotMinutes: 60,
                baselineAggregation: "Mean",
                baselineSmoothingNeighbourWeight: 0.15);

            Assert.NotNull(forecast);
            Assert.True(forecast!.ValueByDayAndHour.TryGetValue(DayOfWeek.Monday, out var bySlot));
            Assert.True(bySlot.TryGetValue(peakSlot, out var v));
            Assert.Equal(90, v, 10);
        }

        [Fact]
        public void ComputeFullForecastFromHistory_TemporalSmoothing_FirstSlotUsesSingleNeighbour()
        {
            var mondayA = new DateTimeOffset(2025, 2, 10, 0, 0, 0, TimeSpan.Zero);
            var mondayB = new DateTimeOffset(2025, 2, 17, 0, 0, 0, TimeSpan.Zero);
            var start = mondayA;
            var end = new DateTimeOffset(2025, 2, 18, 12, 0, 0, TimeSpan.Zero);
            var main = new List<MetricEvalDtoResultValue>
            {
                new MetricEvalDtoResultValue { TimeStamp = mondayA, Average = 40, Maximum = 40, Default = 40 },
                new MetricEvalDtoResultValue { TimeStamp = mondayA.AddHours(1), Average = 100, Maximum = 100, Default = 100 },
                new MetricEvalDtoResultValue { TimeStamp = mondayB, Average = 40, Maximum = 40, Default = 40 },
                new MetricEvalDtoResultValue { TimeStamp = mondayB.AddHours(1), Average = 100, Maximum = 100, Default = 100 },
            };
            var maxSeries = main.Select(m => new MetricEvalDtoResultValue { TimeStamp = m.TimeStamp, Average = 200, Maximum = 200, Default = 200 }).ToList();
            var aggregations = new List<MetricAggregationType> { MetricAggregationType.Average, MetricAggregationType.Maximum };

            var forecast = this.service.ComputeFullForecastFromHistory(
                main,
                maxSeries,
                start,
                end,
                aggregations,
                "m_smooth_edge",
                slotMinutes: 60,
                baselineAggregation: "Mean",
                baselineSmoothingNeighbourWeight: 0.2);

            Assert.NotNull(forecast);
            Assert.True(forecast!.ValueByDayAndHour.TryGetValue(DayOfWeek.Monday, out var bySlot));
            Assert.True(bySlot.TryGetValue(0, out var v));

            // n=1: blended = 0.8*40 + 0.2*100 = 52
            Assert.Equal(52, v, 10);
        }

        [Fact]
        public void ComputeFullForecastFromHistory_TemporalSmoothing_NeighbourWeightClampsAtPointFourNine()
        {
            var mondayA = new DateTimeOffset(2025, 2, 10, 0, 0, 0, TimeSpan.Zero);
            var mondayB = new DateTimeOffset(2025, 2, 17, 0, 0, 0, TimeSpan.Zero);
            var start = mondayA;
            var end = new DateTimeOffset(2025, 2, 18, 12, 0, 0, TimeSpan.Zero);
            var main = new List<MetricEvalDtoResultValue>
            {
                new MetricEvalDtoResultValue { TimeStamp = mondayA.AddHours(10), Average = 50, Maximum = 50, Default = 50 },
                new MetricEvalDtoResultValue { TimeStamp = mondayB.AddHours(10), Average = 50, Maximum = 50, Default = 50 },
            };
            var maxSeries = main.Select(m => new MetricEvalDtoResultValue { TimeStamp = m.TimeStamp, Average = 200, Maximum = 200, Default = 200 }).ToList();
            var aggregations = new List<MetricAggregationType> { MetricAggregationType.Average, MetricAggregationType.Maximum };

            var forecast = this.service.ComputeFullForecastFromHistory(
                main,
                maxSeries,
                start,
                end,
                aggregations,
                "m_clamp",
                slotMinutes: 60,
                baselineAggregation: "Mean",
                baselineSmoothingNeighbourWeight: 10.0);

            Assert.NotNull(forecast);
            Assert.Equal(0.49, forecast!.BaselineSmoothingNeighbourWeightApplied, 10);
        }

        [Fact]
        public void ComputeFullForecastFromHistory_TemporalSmoothing_DisabledLeavesRawAggregates()
        {
            var mondayA = new DateTimeOffset(2025, 2, 10, 0, 0, 0, TimeSpan.Zero);
            var mondayB = new DateTimeOffset(2025, 2, 17, 0, 0, 0, TimeSpan.Zero);
            var start = mondayA;
            var end = new DateTimeOffset(2025, 2, 18, 12, 0, 0, TimeSpan.Zero);
            const int dipSlot = 10;
            var main = new List<MetricEvalDtoResultValue>
            {
                new MetricEvalDtoResultValue { TimeStamp = mondayA.AddHours(9), Average = 100, Maximum = 100, Default = 100 },
                new MetricEvalDtoResultValue { TimeStamp = mondayA.AddHours(10), Average = 50, Maximum = 50, Default = 50 },
                new MetricEvalDtoResultValue { TimeStamp = mondayA.AddHours(11), Average = 100, Maximum = 100, Default = 100 },
                new MetricEvalDtoResultValue { TimeStamp = mondayB.AddHours(9), Average = 100, Maximum = 100, Default = 100 },
                new MetricEvalDtoResultValue { TimeStamp = mondayB.AddHours(10), Average = 50, Maximum = 50, Default = 50 },
                new MetricEvalDtoResultValue { TimeStamp = mondayB.AddHours(11), Average = 100, Maximum = 100, Default = 100 },
            };
            var maxSeries = main.Select(m => new MetricEvalDtoResultValue { TimeStamp = m.TimeStamp, Average = 200, Maximum = 200, Default = 200 }).ToList();
            var aggregations = new List<MetricAggregationType> { MetricAggregationType.Average, MetricAggregationType.Maximum };

            var forecast = this.service.ComputeFullForecastFromHistory(
                main,
                maxSeries,
                start,
                end,
                aggregations,
                "m_no_smooth",
                slotMinutes: 60,
                baselineAggregation: "Mean",
                baselineSmoothingNeighbourWeight: null);

            Assert.NotNull(forecast);
            Assert.Equal(0, forecast!.BaselineSmoothingNeighbourWeightApplied);
            Assert.True(forecast.ValueByDayAndHour.TryGetValue(DayOfWeek.Monday, out var bySlot));
            Assert.True(bySlot.TryGetValue(dipSlot, out var v));
            Assert.Equal(50, v, 10);
            Assert.DoesNotContain(forecast.DiagnosticLines!, l => l.Contains("Temporal smoothing", StringComparison.Ordinal));
        }

        [Fact]
        public void ComputeFullForecastFromHistory_SmoothingRunsBeforeBoost()
        {
            var mondayA = new DateTimeOffset(2025, 2, 10, 0, 0, 0, TimeSpan.Zero);
            var mondayB = new DateTimeOffset(2025, 2, 17, 0, 0, 0, TimeSpan.Zero);
            var start = mondayA;
            var end = new DateTimeOffset(2025, 2, 18, 12, 0, 0, TimeSpan.Zero);
            const int dipSlot = 10;
            var main = new List<MetricEvalDtoResultValue>
            {
                new MetricEvalDtoResultValue { TimeStamp = mondayA.AddHours(9), Average = 100, Maximum = 100, Default = 100 },
                new MetricEvalDtoResultValue { TimeStamp = mondayA.AddHours(10), Average = 50, Maximum = 50, Default = 50 },
                new MetricEvalDtoResultValue { TimeStamp = mondayA.AddHours(11), Average = 100, Maximum = 100, Default = 100 },
                new MetricEvalDtoResultValue { TimeStamp = mondayB.AddHours(9), Average = 100, Maximum = 100, Default = 100 },
                new MetricEvalDtoResultValue { TimeStamp = mondayB.AddHours(10), Average = 50, Maximum = 50, Default = 50 },
                new MetricEvalDtoResultValue { TimeStamp = mondayB.AddHours(11), Average = 100, Maximum = 100, Default = 100 },
            };
            var maxSeries = main.Select(m => new MetricEvalDtoResultValue { TimeStamp = m.TimeStamp, Average = 200, Maximum = 200, Default = 200 }).ToList();
            var aggregations = new List<MetricAggregationType> { MetricAggregationType.Average, MetricAggregationType.Maximum };

            var forecast = this.service.ComputeFullForecastFromHistory(
                main,
                maxSeries,
                start,
                end,
                aggregations,
                "m_order",
                slotMinutes: 60,
                baselineAggregation: "Mean",
                baselineSmoothingNeighbourWeight: 0.15,
                baselineBoostFactor: 2.0);

            Assert.NotNull(forecast);
            Assert.True(forecast!.ValueByDayAndHour.TryGetValue(DayOfWeek.Monday, out var bySlot));
            Assert.True(bySlot.TryGetValue(dipSlot, out var v));

            // Smoothed dip 65 * 2 = 130
            Assert.Equal(130, v, 10);
        }
    }
}
