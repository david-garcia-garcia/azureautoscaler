namespace poolautoscaler.metrics
{
    /// <summary>Azure Monitor custom-metric series bag posted for one metric name.</summary>
    internal sealed class PublishedMetricSeries
    {
        /// <summary>Initializes a new instance of the <see cref="PublishedMetricSeries"/> class.</summary>
        /// <param name="name">Metric name in Azure Monitor.</param>
        /// <param name="min">Lowest sample in the published interval.</param>
        /// <param name="max">Highest sample in the published interval.</param>
        /// <param name="sum">Sum of samples in the published interval.</param>
        /// <param name="count">Number of samples in the published interval.</param>
        public PublishedMetricSeries(string name, double min, double max, double sum, int count)
        {
            this.Name = name;
            this.Min = min;
            this.Max = max;
            this.Sum = sum;
            this.Count = count;
        }

        /// <summary>Metric name in Azure Monitor.</summary>
        public string Name { get; }

        /// <summary>Lowest sample in the published interval.</summary>
        public double Min { get; }

        /// <summary>Highest sample in the published interval.</summary>
        public double Max { get; }

        /// <summary>Sum of samples in the published interval.</summary>
        public double Sum { get; }

        /// <summary>Number of samples in the published interval.</summary>
        public int Count { get; }

        /// <summary>Builds a one-sample series from a single published value.</summary>
        /// <param name="name">Metric name in Azure Monitor.</param>
        /// <param name="value">Value copied into min, max, and sum with count 1.</param>
        /// <returns>A series whose Average, Minimum, and Maximum are that value.</returns>
        public static PublishedMetricSeries FromScalar(string name, double value)
        {
            return new PublishedMetricSeries(name, value, value, value, 1);
        }
    }
}
