namespace poolautoscaler.metrics
{
    /// <summary>One named cell from the first Query result row.</summary>
    internal sealed class SqlQueryColumn
    {
        /// <summary>Initializes a new instance of the <see cref="SqlQueryColumn"/> class.</summary>
        /// <param name="name">Column name used as the Azure Monitor metric name when numeric.</param>
        /// <param name="value">Cell value from the first row, or null.</param>
        /// <param name="providerType">CLR type reported by the data reader for this column.</param>
        public SqlQueryColumn(string name, object? value, Type providerType)
        {
            this.Name = name;
            this.Value = value;
            this.ProviderType = providerType;
        }

        /// <summary>Column name from the result set.</summary>
        public string Name { get; }

        /// <summary>First-row cell value, or null when the provider returned DBNull.</summary>
        public object? Value { get; }

        /// <summary>Provider CLR type for the column.</summary>
        public Type ProviderType { get; }
    }
}
