namespace poolautoscaler.metrics
{
    /// <summary>
    /// Host, catalog, engine, and timeout facts needed to open one Query session.
    /// Built from a refreshed SQL resource state; never from YAML host or Database knobs.
    /// </summary>
    internal sealed class SqlQueryConnectionPlan
    {
        /// <summary>Initializes a new instance of the <see cref="SqlQueryConnectionPlan"/> class.</summary>
        /// <param name="engine">SQL engine for this resource.</param>
        /// <param name="host">ARM FullyQualifiedDomainName.</param>
        /// <param name="catalog">Implied catalog for the resource type.</param>
        /// <param name="connectionString">Driver connection string (no password).</param>
        /// <param name="tokenScope">TokenCredential scope for this engine.</param>
        /// <param name="commandTimeout">SQL command timeout.</param>
        public SqlQueryConnectionPlan(
            SqlQueryEngine engine,
            string host,
            string catalog,
            string connectionString,
            string tokenScope,
            TimeSpan commandTimeout)
        {
            this.Engine = engine;
            this.Host = host;
            this.Catalog = catalog;
            this.ConnectionString = connectionString;
            this.TokenScope = tokenScope;
            this.CommandTimeout = commandTimeout;
        }

        /// <summary>SQL engine selected from the ResourceStateFactory regexes.</summary>
        public SqlQueryEngine Engine { get; }

        /// <summary>SQL host FQDN from refreshed ARM.</summary>
        public string Host { get; }

        /// <summary>Implied catalog for this resource type.</summary>
        public string Catalog { get; }

        /// <summary>Driver connection string without a password or access token.</summary>
        public string ConnectionString { get; }

        /// <summary>AAD token scope for this engine.</summary>
        public string TokenScope { get; }

        /// <summary>Command timeout applied to the Query.</summary>
        public TimeSpan CommandTimeout { get; }
    }
}
