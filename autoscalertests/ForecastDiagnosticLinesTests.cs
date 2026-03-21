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
        }
    }
}
