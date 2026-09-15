namespace poolautoscaler.metrics
{
    /// <summary>Decides which first-row columns are published as Azure Monitor custom metrics.</summary>
    internal static class SqlQueryNumericColumns
    {
        /// <summary>
        /// Returns published metric values: numeric CLR types or boxed numbers. Nulls, strings, dates, guids, and bools are omitted.
        /// </summary>
        /// <param name="columns">First-row columns from the Query.</param>
        /// <returns>Column name and double value for each publishable cell.</returns>
        public static IReadOnlyList<(string Name, double Value)> SelectPublished(IEnumerable<SqlQueryColumn> columns)
        {
            var published = new List<(string Name, double Value)>();
            foreach (var column in columns)
            {
                if (!TryConvertNumeric(column, out var numericValue))
                {
                    continue;
                }

                published.Add((column.Name, numericValue));
            }

            return published;
        }

        /// <summary>Converts a column to a published double when it is a CLR numeric type or a boxed number.</summary>
        /// <param name="column">First-row column.</param>
        /// <param name="numericValue">Converted value when the method returns true.</param>
        /// <returns>True when the column should be published.</returns>
        public static bool TryConvertNumeric(SqlQueryColumn column, out double numericValue)
        {
            numericValue = 0;
            if (column.Value is null)
            {
                return false;
            }

            if (IsIgnoredNonNumeric(column.Value.GetType()) || IsIgnoredNonNumeric(column.ProviderType))
            {
                return false;
            }

            if (IsClrNumericType(column.Value.GetType()) || IsClrNumericType(column.ProviderType))
            {
                numericValue = Convert.ToDouble((IConvertible)column.Value);
                return true;
            }

            return false;
        }

        /// <summary>Returns whether the type is a CLR numeric type used as a metric value.</summary>
        /// <param name="type">CLR type of the provider field or boxed value.</param>
        /// <returns>True for byte, short, int, long, float, double, or decimal (and their unsigned siblings).</returns>
        private static bool IsClrNumericType(Type type)
        {
            var underlying = Nullable.GetUnderlyingType(type) ?? type;
            return underlying == typeof(byte)
                || underlying == typeof(sbyte)
                || underlying == typeof(short)
                || underlying == typeof(ushort)
                || underlying == typeof(int)
                || underlying == typeof(uint)
                || underlying == typeof(long)
                || underlying == typeof(ulong)
                || underlying == typeof(float)
                || underlying == typeof(double)
                || underlying == typeof(decimal);
        }

        /// <summary>Returns whether the type is a non-numeric shape that must not become a metric.</summary>
        /// <param name="type">CLR type to classify.</param>
        /// <returns>True for string, date/time, guid, or bool.</returns>
        private static bool IsIgnoredNonNumeric(Type type)
        {
            var underlying = Nullable.GetUnderlyingType(type) ?? type;
            return underlying == typeof(string)
                || underlying == typeof(DateTime)
                || underlying == typeof(DateTimeOffset)
                || underlying == typeof(Guid)
                || underlying == typeof(bool)
                || underlying == typeof(char);
        }
    }
}
