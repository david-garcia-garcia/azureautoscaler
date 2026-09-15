using poolautoscaler.metrics;

namespace poolautoscaler.tests
{
    /// <summary>Groups Query numeric columns into one-sample series or suffix series bags.</summary>
    public class SqlQueryMetricSeriesMapperTests
    {
        [Fact]
        public void Map_BareNumericColumns_AreOneSampleSeries()
        {
            var mapped = SqlQueryMetricSeriesMapper.Map(
                new[]
                {
                    new SqlQueryColumn("cpu_percent", 10.0d, typeof(double)),
                    new SqlQueryColumn("from_time", DateTime.UtcNow, typeof(DateTime)),
                });

            Assert.Single(mapped.Series);
            Assert.Empty(mapped.SkippedMetricNames);
            var series = mapped.Series[0];
            Assert.Equal("cpu_percent", series.Name);
            Assert.Equal(10.0d, series.Min);
            Assert.Equal(10.0d, series.Max);
            Assert.Equal(10.0d, series.Sum);
            Assert.Equal(1, series.Count);
        }

        [Fact]
        public void Map_CompleteSuffixGroup_FillsMinMaxSumCount()
        {
            var mapped = SqlQueryMetricSeriesMapper.Map(
                new[]
                {
                    new SqlQueryColumn("cpu_percent_min", 2.0d, typeof(double)),
                    new SqlQueryColumn("cpu_percent_max", 18.0d, typeof(double)),
                    new SqlQueryColumn("cpu_percent_sum", 60.0d, typeof(double)),
                    new SqlQueryColumn("cpu_percent_count", 12, typeof(int)),
                });

            Assert.Single(mapped.Series);
            Assert.Empty(mapped.SkippedMetricNames);
            var series = mapped.Series[0];
            Assert.Equal("cpu_percent", series.Name);
            Assert.Equal(2.0d, series.Min);
            Assert.Equal(18.0d, series.Max);
            Assert.Equal(60.0d, series.Sum);
            Assert.Equal(12, series.Count);
        }

        [Fact]
        public void Map_IncompleteSuffixGroup_SkipsStem()
        {
            var mapped = SqlQueryMetricSeriesMapper.Map(
                new[]
                {
                    new SqlQueryColumn("cpu_percent_min", 2.0d, typeof(double)),
                    new SqlQueryColumn("cpu_percent_max", 18.0d, typeof(double)),
                });

            Assert.Empty(mapped.Series);
            Assert.Equal(new[] { "cpu_percent" }, mapped.SkippedMetricNames);
        }

        [Fact]
        public void Map_BareColumnMatchingSuffixStem_SkipsScalarAndKeepsSeries()
        {
            var mapped = SqlQueryMetricSeriesMapper.Map(
                new[]
                {
                    new SqlQueryColumn("cpu_percent", 9.0d, typeof(double)),
                    new SqlQueryColumn("cpu_percent_min", 2.0d, typeof(double)),
                    new SqlQueryColumn("cpu_percent_max", 18.0d, typeof(double)),
                    new SqlQueryColumn("cpu_percent_sum", 60.0d, typeof(double)),
                    new SqlQueryColumn("cpu_percent_count", 12.0d, typeof(double)),
                });

            Assert.Single(mapped.Series);
            Assert.Equal(new[] { "cpu_percent" }, mapped.SkippedMetricNames);
            Assert.Equal(2.0d, mapped.Series[0].Min);
            Assert.Equal(12, mapped.Series[0].Count);
        }

        [Fact]
        public void Map_CountBelowOne_SkipsStem()
        {
            var mapped = SqlQueryMetricSeriesMapper.Map(
                new[]
                {
                    new SqlQueryColumn("cpu_percent_min", 0.0d, typeof(double)),
                    new SqlQueryColumn("cpu_percent_max", 0.0d, typeof(double)),
                    new SqlQueryColumn("cpu_percent_sum", 0.0d, typeof(double)),
                    new SqlQueryColumn("cpu_percent_count", 0, typeof(int)),
                });

            Assert.Empty(mapped.Series);
            Assert.Equal(new[] { "cpu_percent" }, mapped.SkippedMetricNames);
        }
    }
}
