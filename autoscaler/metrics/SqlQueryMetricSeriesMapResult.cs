namespace poolautoscaler.metrics
{
    /// <summary>Publishable Query series plus stems the mapper did not post.</summary>
    internal sealed class SqlQueryMetricSeriesMapResult
    {
        /// <summary>Initializes a new instance of the <see cref="SqlQueryMetricSeriesMapResult"/> class.</summary>
        /// <param name="series">Series that can be POSTed.</param>
        /// <param name="skippedMetricNames">Stems skipped as incomplete, duplicate, invalid count, or scalar conflict.</param>
        public SqlQueryMetricSeriesMapResult(
            IReadOnlyList<PublishedMetricSeries> series,
            IReadOnlyList<string> skippedMetricNames)
        {
            this.Series = series;
            this.SkippedMetricNames = skippedMetricNames;
        }

        /// <summary>Series that can be POSTed.</summary>
        public IReadOnlyList<PublishedMetricSeries> Series { get; }

        /// <summary>Stems skipped as incomplete, duplicate, invalid count, or scalar conflict.</summary>
        public IReadOnlyList<string> SkippedMetricNames { get; }
    }
}
