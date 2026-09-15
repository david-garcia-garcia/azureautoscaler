namespace poolautoscaler.metrics
{
    /// <summary>Groups first-row numeric Query columns into Azure Monitor series bags.</summary>
    internal static class SqlQueryMetricSeriesMapper
    {
        /// <summary>Reserved suffix for the series minimum.</summary>
        internal const string MinSuffix = "_min";

        /// <summary>Reserved suffix for the series maximum.</summary>
        internal const string MaxSuffix = "_max";

        /// <summary>Reserved suffix for the series sum.</summary>
        internal const string SumSuffix = "_sum";

        /// <summary>Reserved suffix for the series sample count.</summary>
        internal const string CountSuffix = "_count";

        private const string MinField = "min";
        private const string MaxField = "max";
        private const string SumField = "sum";
        private const string CountField = "count";
        private const string DuplicateField = "__duplicate";

        /// <summary>
        /// Maps numeric columns to series. Bare names stay one-sample. Matching _min/_max/_sum/_count suffixes become one stem.
        /// </summary>
        /// <param name="columns">First-row columns from the Query.</param>
        /// <returns>Publishable series and stems skipped as incomplete, duplicate, or conflicting.</returns>
        public static SqlQueryMetricSeriesMapResult Map(IEnumerable<SqlQueryColumn> columns)
        {
            var published = SqlQueryNumericColumns.SelectPublished(columns);
            var seriesFields = new Dictionary<string, Dictionary<string, double>>(StringComparer.Ordinal);
            var seriesOrder = new List<string>();
            var scalars = new List<(string Name, double Value)>();

            // Split numeric columns into suffix fields (grouped by stem) and bare one-sample names.
            foreach (var (columnName, numericValue) in published)
            {
                if (TrySplitSeriesField(columnName, out var stem, out var field))
                {
                    if (!seriesFields.TryGetValue(stem, out var fields))
                    {
                        fields = new Dictionary<string, double>(StringComparer.Ordinal);
                        seriesFields[stem] = fields;
                        seriesOrder.Add(stem);
                    }

                    if (fields.ContainsKey(field))
                    {
                        fields[DuplicateField] = 1;
                    }
                    else
                    {
                        fields[field] = numericValue;
                    }

                    continue;
                }

                scalars.Add((columnName, numericValue));
            }

            var series = new List<CustomMetricSeries>();
            var skippedMetricNames = new HashSet<string>(StringComparer.Ordinal);

            // Bare names stay one-sample unless the same stem also has suffix columns.
            foreach (var (scalarName, scalarValue) in scalars)
            {
                if (seriesFields.ContainsKey(scalarName))
                {
                    skippedMetricNames.Add(scalarName);
                    continue;
                }

                series.Add(CustomMetricSeries.FromScalar(scalarName, scalarValue));
            }

            // Complete suffix groups become one series; incomplete, duplicate, or invalid count stems are skipped.
            foreach (var stem in seriesOrder)
            {
                var fields = seriesFields[stem];
                if (fields.ContainsKey(DuplicateField)
                    || !fields.ContainsKey(MinField)
                    || !fields.ContainsKey(MaxField)
                    || !fields.ContainsKey(SumField)
                    || !fields.ContainsKey(CountField)
                    || !TryReadSampleCount(fields[CountField], out var sampleCount))
                {
                    skippedMetricNames.Add(stem);
                    continue;
                }

                series.Add(new CustomMetricSeries(stem, fields[MinField], fields[MaxField], fields[SumField], sampleCount));
            }

            return new SqlQueryMetricSeriesMapResult(series, skippedMetricNames.ToList());
        }

        /// <summary>Splits a reserved suffix column into stem and series field. Empty stems are not series fields.</summary>
        /// <param name="columnName">Numeric column name.</param>
        /// <param name="stem">Metric name without the suffix when the method returns true.</param>
        /// <param name="field">min, max, sum, or count when the method returns true.</param>
        /// <returns>True when the name ends with a reserved series suffix and has a stem.</returns>
        internal static bool TrySplitSeriesField(string columnName, out string stem, out string field)
        {
            stem = string.Empty;
            field = string.Empty;
            if (TryStripSuffix(columnName, MinSuffix, out stem))
            {
                field = MinField;
                return true;
            }

            if (TryStripSuffix(columnName, MaxSuffix, out stem))
            {
                field = MaxField;
                return true;
            }

            if (TryStripSuffix(columnName, SumSuffix, out stem))
            {
                field = SumField;
                return true;
            }

            if (TryStripSuffix(columnName, CountSuffix, out stem))
            {
                field = CountField;
                return true;
            }

            return false;
        }

        /// <summary>Accepts a finite count of at least one sample and rounds to the nearest integer.</summary>
        /// <param name="countValue">Numeric _count column.</param>
        /// <param name="sampleCount">Rounded sample count when the method returns true.</param>
        /// <returns>True when the value can be posted as Azure Monitor count.</returns>
        private static bool TryReadSampleCount(double countValue, out int sampleCount)
        {
            sampleCount = 0;
            if (double.IsNaN(countValue) || double.IsInfinity(countValue) || countValue < 1)
            {
                return false;
            }

            var rounded = (int)Math.Round(countValue, MidpointRounding.AwayFromZero);
            if (rounded < 1)
            {
                return false;
            }

            sampleCount = rounded;
            return true;
        }

        /// <summary>Strips one reserved suffix when the leftover stem is non-empty.</summary>
        /// <param name="columnName">Numeric column name.</param>
        /// <param name="suffix">Reserved suffix including the leading underscore.</param>
        /// <param name="stem">Name without the suffix when the method returns true.</param>
        /// <returns>True when the name ends with the suffix and the stem is not empty.</returns>
        private static bool TryStripSuffix(string columnName, string suffix, out string stem)
        {
            stem = string.Empty;
            if (columnName.Length <= suffix.Length || !columnName.EndsWith(suffix, StringComparison.Ordinal))
            {
                return false;
            }

            stem = columnName[..^suffix.Length];
            return stem.Length > 0;
        }
    }
}
