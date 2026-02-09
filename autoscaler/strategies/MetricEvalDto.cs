using Azure.Monitor.Query.Models;

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

        /// <summary>
        /// Indicates whether this metric data is valid and can be trusted for scaling decisions.
        /// If false, the metric is considered broken/unreliable and should not be used.
        /// </summary>
        public bool Valid { get; set; } = true;

        /// <summary>
        /// Optional message explaining why the metric is invalid
        /// </summary>
        public string InvalidReason { get; set; }

        /// <summary>
        /// The aggregations that were actually executed when retrieving this metric.
        /// This is the effective list after applying defaults (defaults to Average if not specified).
        /// </summary>
        public IList<MetricAggregationType> ExecutedAggregations { get; set; }

        /// <summary>
        /// The primary aggregation type used for the Default property in values.
        /// This is the first aggregation from ExecutedAggregations.
        /// </summary>
        public MetricAggregationType? PrimaryAggregation => 
            ExecutedAggregations != null && ExecutedAggregations.Any() 
                ? ExecutedAggregations.First() 
                : null;
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

        /// <summary>
        /// The default/primary value for this metric data point. This is automatically populated with 
        /// whichever aggregation is available (Average, Maximum, Minimum, Total, or Count in that priority order).
        /// Use this in scaling rules to avoid having to update expressions when changing aggregation types.
        /// </summary>
        public double? Default { get; set; }

        /// <summary>
        /// Indicates whether this individual data point is valid based on configured validation rules.
        /// If false, this data point failed validation (e.g., out of min/max bounds).
        /// </summary>
        public bool Valid { get; set; } = true;

        /// <summary>
        /// Optional reason why this data point is invalid
        /// </summary>
        public string InvalidReason { get; set; }

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
            this.Default = this.Average ?? this.Maximum ?? this.Minimum ?? this.Total ?? this.Count;
            return this;
        }

        public MetricEvalDtoResultValue SetMinimum(double? Minimum)
        {
            this.Minimum = Minimum;
            this.Default = this.Average ?? this.Maximum ?? this.Minimum ?? this.Total ?? this.Count;
            return this;
        }

        public MetricEvalDtoResultValue SetAverage(double? Average)
        {
            this.Average = Average;
            this.Default = this.Average ?? this.Maximum ?? this.Minimum ?? this.Total ?? this.Count;
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
            this.Default = this.Average ?? this.Maximum ?? this.Minimum ?? this.Total ?? this.Count;
            return this;
        }

        public MetricEvalDtoResultValue SetCount(double? Count)
        {
            this.Count = Count;
            this.Default = this.Average ?? this.Maximum ?? this.Minimum ?? this.Total ?? this.Count;
            return this;
        }

        /// <summary>
        /// Gets the aggregation type for this value (avg, max, min, total, count, custom, or unknown)
        /// </summary>
        public string GetAggregationType()
        {
            if (this.Average.HasValue) return "avg";
            if (this.Maximum.HasValue) return "max";
            if (this.Minimum.HasValue) return "min";
            if (this.Total.HasValue) return "total";
            if (this.Count.HasValue) return "count";
            if (!string.IsNullOrEmpty(this.CustomString)) return "custom";
            return "unknown";
        }

        /// <summary>
        /// Renders the value as a formatted string based on its type
        /// Removes unnecessary decimal places if the value is a whole number
        /// </summary>
        public string RenderValue()
        {
            if (this.Average.HasValue) return FormatNumber(this.Average.Value);
            if (this.Maximum.HasValue) return FormatNumber(this.Maximum.Value);
            if (this.Minimum.HasValue) return FormatNumber(this.Minimum.Value);
            if (this.Total.HasValue) return FormatNumber(this.Total.Value);
            if (this.Count.HasValue) return this.Count.Value.ToString("F0", System.Globalization.CultureInfo.InvariantCulture);
            if (!string.IsNullOrEmpty(this.CustomString)) return this.CustomString;
            return "null";
        }

        private static string FormatNumber(double value)
        {
            // Use InvariantCulture to ensure consistent decimal separator (.) regardless of system locale
            // If the value is a whole number, don't show decimals
            if (value == Math.Floor(value))
            {
                return value.ToString("F0", System.Globalization.CultureInfo.InvariantCulture);
            }
            // Otherwise show 2 decimal places
            return value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Renders the value with validity status (prefixes invalid values with !)
        /// </summary>
        public string RenderValueWithStatus()
        {
            var val = RenderValue();
            return this.Valid ? val : $"!{val}";
        }
    }
}
