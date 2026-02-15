namespace poolautoscaler.metrics.Dto
{
    /// <summary>Single metric data point (aggregations, timestamp, validity).</summary>
    public class MetricEvalDtoResultValue
    {
        /// <summary>Max value in range.</summary>
        public double? Maximum { get; set; }

        /// <summary>Min value in range.</summary>
        public double? Minimum { get; set; }

        /// <summary>Average value.</summary>
        public double? Average { get; set; }

        /// <summary>Custom string value (e.g. SKU name).</summary>
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
        /// Optional reason why this data point is invalid.
        /// </summary>
        public string InvalidReason { get; set; }

        /// <summary>Returns whether this result has any data (max, min, average, custom string, total, or count).</summary>
        /// <returns>True if any value is set.</returns>
        public bool HasData()
        {
            return this.Maximum.HasValue ||
                   this.Minimum.HasValue ||
                   this.Average.HasValue ||
                   !string.IsNullOrEmpty(this.CustomString) ||
                   this.Total.HasValue ||
                   this.Count.HasValue;
        }

        /// <summary>Sets maximum and returns this instance.</summary>
        /// <param name="Maximum">The maximum value.</param>
        /// <returns>This instance for chaining.</returns>
        public MetricEvalDtoResultValue SetMaximum(double? Maximum)
        {
            this.Maximum = Maximum;
            return this;
        }

        /// <summary>Sets minimum and returns this instance.</summary>
        /// <param name="Minimum">The minimum value.</param>
        /// <returns>This instance for chaining.</returns>
        public MetricEvalDtoResultValue SetMinimum(double? Minimum)
        {
            this.Minimum = Minimum;
            return this;
        }

        /// <summary>Sets average and returns this instance.</summary>
        /// <param name="Average">The average value.</param>
        /// <returns>This instance for chaining.</returns>
        public MetricEvalDtoResultValue SetAverage(double? Average)
        {
            this.Average = Average;
            return this;
        }

        /// <summary>Sets custom string and returns this instance.</summary>
        /// <param name="CustomString">The custom string value.</param>
        /// <returns>This instance for chaining.</returns>
        public MetricEvalDtoResultValue SetCustomString(string CustomString)
        {
            this.CustomString = CustomString;
            return this;
        }

        /// <summary>Sets total and returns this instance.</summary>
        /// <param name="Total">The total value.</param>
        /// <returns>This instance for chaining.</returns>
        public MetricEvalDtoResultValue SetTotal(double? Total)
        {
            this.Total = Total;
            return this;
        }

        /// <summary>Sets count and returns this instance.</summary>
        /// <param name="Count">The count value.</param>
        /// <returns>This instance for chaining.</returns>
        public MetricEvalDtoResultValue SetCount(double? Count)
        {
            this.Count = Count;
            return this;
        }

        /// <summary>
        /// Gets the aggregation type for this value (avg, max, min, total, count, custom, or unknown).
        /// </summary>
        /// <returns>Aggregation type string.</returns>
        public string GetAggregationType()
        {
            if (this.Average.HasValue)
            {
                return "avg";
            }

            if (this.Maximum.HasValue)
            {
                return "max";
            }

            if (this.Minimum.HasValue)
            {
                return "min";
            }

            if (this.Total.HasValue)
            {
                return "total";
            }

            if (this.Count.HasValue)
            {
                return "count";
            }

            if (!string.IsNullOrEmpty(this.CustomString))
            {
                return "custom";
            }

            return "unknown";
        }

        /// <summary>
        /// Renders the value as a formatted string based on its type.
        /// Removes unnecessary decimal places if the value is a whole number.
        /// </summary>
        /// <returns>Formatted value string.</returns>
        public string RenderValue()
        {
            if (this.Average.HasValue)
            {
                return FormatNumber(this.Average.Value);
            }

            if (this.Maximum.HasValue)
            {
                return FormatNumber(this.Maximum.Value);
            }

            if (this.Minimum.HasValue)
            {
                return FormatNumber(this.Minimum.Value);
            }

            if (this.Total.HasValue)
            {
                return FormatNumber(this.Total.Value);
            }

            if (this.Count.HasValue)
            {
                return this.Count.Value.ToString("F0", System.Globalization.CultureInfo.InvariantCulture);
            }

            if (!string.IsNullOrEmpty(this.CustomString))
            {
                return this.CustomString;
            }

            return "null";
        }

        /// <summary>
        /// Renders the value with validity status (prefixes invalid values with !).
        /// </summary>
        /// <returns>Formatted value with optional ! prefix if invalid.</returns>
        public string RenderValueWithStatus()
        {
            var val = this.RenderValue();
            return this.Valid ? val : $"!{val}";
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
    }
}
