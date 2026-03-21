using System.Collections.ObjectModel;

namespace poolautoscaler.metrics.Dto
{
    /// <summary>
    /// Summary of how the baseline forecast was built from history (date range, sample counts).
    /// Use for structured diagnostics and to avoid duplicating reliability logging text.
    /// </summary>
    public sealed class ForecastBuildInfo
    {
        private static readonly DayOfWeek[] DaysMondayFirst =
        {
            DayOfWeek.Monday,
            DayOfWeek.Tuesday,
            DayOfWeek.Wednesday,
            DayOfWeek.Thursday,
            DayOfWeek.Friday,
            DayOfWeek.Saturday,
            DayOfWeek.Sunday,
        };

        /// <summary>
        /// Initializes a new instance of the <see cref="ForecastBuildInfo"/> class.
        /// </summary>
        /// <param name="startDateUtc">Start of the history window (UTC).</param>
        /// <param name="endDateUtc">End of the history window (UTC).</param>
        /// <param name="daysWithDataCount">Number of calendar days in the window that had at least one daily window.</param>
        /// <param name="samplesPerDayOfWeek">Count of contributing daily windows per day of week.</param>
        public ForecastBuildInfo(
            DateTimeOffset startDateUtc,
            DateTimeOffset endDateUtc,
            int daysWithDataCount,
            IReadOnlyDictionary<DayOfWeek, int> samplesPerDayOfWeek)
        {
            this.StartDateUtc = startDateUtc;
            this.EndDateUtc = endDateUtc;
            this.DaysWithDataCount = daysWithDataCount;
            this.SamplesPerDayOfWeek = new ReadOnlyDictionary<DayOfWeek, int>(
                new Dictionary<DayOfWeek, int>(samplesPerDayOfWeek));
            this.TotalWeeksAnalyzed = (endDateUtc - startDateUtc).TotalDays / 7.0;
        }

        /// <summary>Start of the analyzed history range (UTC).</summary>
        public DateTimeOffset StartDateUtc { get; }

        /// <summary>End of the analyzed history range (UTC).</summary>
        public DateTimeOffset EndDateUtc { get; }

        /// <summary>Number of days in the range that had usable data.</summary>
        public int DaysWithDataCount { get; }

        /// <summary>Length of the history window expressed in weeks.</summary>
        public double TotalWeeksAnalyzed { get; }

        /// <summary>How many daily windows contributed per day of week.</summary>
        public IReadOnlyDictionary<DayOfWeek, int> SamplesPerDayOfWeek { get; }

        /// <summary>
        /// Appends the standard reliability lines (weeks analyzed, date range, samples per day of week)
        /// to <paramref name="lines"/> in the same format as the full weekly forecast diagnostic block.
        /// </summary>
        /// <param name="lines">Target list (e.g. <see cref="MetricForecastResult.DiagnosticLines"/> builder).</param>
        public void AppendReliabilityDiagnosticLines(ICollection<string> lines)
        {
            lines.Add(
                $"Reliability: Analyzed {this.TotalWeeksAnalyzed:F1} weeks ({this.StartDateUtc:yyyy-MM-dd} to {this.EndDateUtc:yyyy-MM-dd} UTC). {this.DaysWithDataCount} days had data.");
            var samplesByDay = string.Join(
                ", ",
                DaysMondayFirst.Select(d => this.SamplesPerDayOfWeek.TryGetValue(d, out var n) ? $"{d}: {n}" : $"{d}: 0"));
            lines.Add($"Samples per day of week: {samplesByDay}.");
        }
    }
}
