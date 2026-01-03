namespace poolautoscaler.strategies
{
    public class MetricEvalDto
    {
        public Dictionary<string, MetricEvalDtoResult> Metrics = new Dictionary<string, MetricEvalDtoResult>();

        // For dimensions that are not discrete (i.e. tiers) this is the way to evaluate step up or down
        public Func<long, string> _NextDimensionValue { get; set; }

        // For dimensions that are not discrete (i.e. tiers) this is the way to evaluate step up or down
        public Func<long, string> _PreviousDimensionValue { get; set; }

        public string NextDimensionValue(long step)
        {
            return this._NextDimensionValue(step);
        }

        public string PreviousDimensionValue(long step)
        {
            return this._PreviousDimensionValue(step);
        }
    }

    public class MetricEvalDtoResult
    {
        /// <summary>
        /// For metrics that provide a range of values
        /// </summary>
        public List<MetricEvalDtoResultValue> Values = new List<MetricEvalDtoResultValue>();
    }

    public class MetricEvalDtoResultValue
    {
        public double? Maximum { get; set; }

        public double? Minimum { get; set; }

        public double? Average { get; set; }

        public string CustomString { get; set; }

        /// <summary> The timestamp for the metric value in ISO 8601 format. </summary>
        public DateTimeOffset TimeStamp { get; set; }

        /// <summary> The average value in the time range. </summary>
        /// <summary> The sum of all of the values in the time range. </summary>
        public double? Total { get; set; }

        /// <summary> The number of samples in the time range. Can be used to determine the number of values that contributed to the average value. </summary>
        public double? Count { get; set; }

        public bool HasData()
        {
            return Maximum.HasValue || 
                   Minimum.HasValue || 
                   Average.HasValue || 
                   !string.IsNullOrEmpty(CustomString) ||
                   Total.HasValue ||
                   Count.HasValue;
        }

        public MetricEvalDtoResultValue SetMaximum(double? Maximum)
        {
            this.Maximum = Maximum;
            return this;
        }

        public MetricEvalDtoResultValue SetMinimum(double? Minimum)
        {
            this.Minimum = Minimum;
            return this;
        }

        public MetricEvalDtoResultValue SetAverage(double? Average)
        {
            this.Average = Average;
            return this;
        }

        public MetricEvalDtoResultValue SetCustomString(string CustomString)
        {
            this.CustomString = CustomString;
            return this;
        }

        public MetricEvalDtoResultValue SetTotal(double? Total)
        {
            this.Total = Total;
            return this;
        }

        public MetricEvalDtoResultValue SetCount(double? Count)
        {
            this.Count = Count;
            return this;
        }
    }
}
