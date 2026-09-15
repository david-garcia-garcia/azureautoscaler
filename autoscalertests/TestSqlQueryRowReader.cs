using poolautoscaler.metrics;

namespace poolautoscaler.tests
{
    /// <summary>In-memory Query reader for tests. Does not open a live SQL session.</summary>
    internal sealed class TestSqlQueryRowReader : ISqlQueryRowReader
    {
        private readonly IReadOnlyList<SqlQueryColumn> firstRow;
        private readonly Func<string, Exception?>? exceptionForQuery;

        /// <summary>Initializes a new instance of the <see cref="TestSqlQueryRowReader"/> class that returns the given first row.</summary>
        /// <param name="firstRow">Columns of the first row, or empty.</param>
        public TestSqlQueryRowReader(IReadOnlyList<SqlQueryColumn> firstRow)
        {
            this.firstRow = firstRow;
        }

        /// <summary>Initializes a new instance of the <see cref="TestSqlQueryRowReader"/> class that throws for selected Query text.</summary>
        /// <param name="firstRow">Columns returned when the Query is not rejected.</param>
        /// <param name="exceptionForQuery">Returns an exception to throw for that Query text, or null to return <paramref name="firstRow"/>.</param>
        public TestSqlQueryRowReader(IReadOnlyList<SqlQueryColumn> firstRow, Func<string, Exception?> exceptionForQuery)
        {
            this.firstRow = firstRow;
            this.exceptionForQuery = exceptionForQuery;
        }

        /// <summary>How many times the factory asked this reader to run.</summary>
        public int ReadCount { get; private set; }

        /// <summary>Last Query text the factory passed through.</summary>
        public string? LastQuery { get; private set; }

        /// <inheritdoc />
        public Task<IReadOnlyList<SqlQueryColumn>> ReadFirstRowAsync(
            SqlQueryConnectionPlan plan,
            string accessToken,
            string? entraUserName,
            string query,
            CancellationToken cancellationToken)
        {
            this.ReadCount++;
            this.LastQuery = query;
            var failure = this.exceptionForQuery?.Invoke(query);
            if (failure != null)
            {
                throw failure;
            }

            return Task.FromResult(this.firstRow);
        }
    }
}
