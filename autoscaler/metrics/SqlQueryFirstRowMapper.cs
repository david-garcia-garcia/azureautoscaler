using System.Data;

namespace poolautoscaler.metrics
{
    /// <summary>Maps an ADO first row into named <see cref="SqlQueryColumn"/> values.</summary>
    internal static class SqlQueryFirstRowMapper
    {
        /// <summary>Reads only the current first row from the reader. Does not advance past that row.</summary>
        /// <param name="reader">Open data reader positioned before the first row, or already on it.</param>
        /// <param name="alreadyOnFirstRow">True when the caller has already consumed the first Read().</param>
        /// <returns>Columns of the first row, or empty when there is no row.</returns>
        public static IReadOnlyList<SqlQueryColumn> ReadFirstRow(IDataReader reader, bool alreadyOnFirstRow = false)
        {
            if (!alreadyOnFirstRow && !reader.Read())
            {
                return Array.Empty<SqlQueryColumn>();
            }

            var columns = new List<SqlQueryColumn>(reader.FieldCount);
            for (var columnIndex = 0; columnIndex < reader.FieldCount; columnIndex++)
            {
                var columnName = reader.GetName(columnIndex);
                var providerType = reader.GetFieldType(columnIndex);
                var cellValue = reader.IsDBNull(columnIndex) ? null : reader.GetValue(columnIndex);
                columns.Add(new SqlQueryColumn(columnName, cellValue, providerType));
            }

            return columns;
        }
    }
}
