namespace poolautoscaler.metrics
{
    /// <summary>
    /// Reads the first result row of a Query. Production uses ADO drivers; tests inject an in-memory reader.
    /// </summary>
    internal interface ISqlQueryRowReader
    {
        /// <summary>Executes the operator SQL once and returns the first row's columns, or an empty list.</summary>
        /// <param name="plan">Connection facts for this resource.</param>
        /// <param name="accessToken">Entra access token for the engine scope.</param>
        /// <param name="entraUserName">Display or app name from the token when the driver requires a user name.</param>
        /// <param name="query">Operator SQL text, executed as configured.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Columns of the first row, or empty when the result has no rows.</returns>
        Task<IReadOnlyList<SqlQueryColumn>> ReadFirstRowAsync(
            SqlQueryConnectionPlan plan,
            string accessToken,
            string? entraUserName,
            string query,
            CancellationToken cancellationToken);
    }
}
