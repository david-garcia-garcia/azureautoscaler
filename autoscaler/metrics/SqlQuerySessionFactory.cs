using Azure.Core;
using poolautoscaler.resourcemanagement;
using poolautoscaler.resources.MsSqlDatabase;
using poolautoscaler.resources.MssqlElasticPool;
using poolautoscaler.resources.MySqlFlexibleServer;
using poolautoscaler.resources.PostgreSqlFlexibleServer;

namespace poolautoscaler.metrics
{
    /// <summary>
    /// Builds a Query session from a refreshed SQL resource and returns the first row's name/value pairs.
    /// </summary>
    internal sealed class SqlQuerySessionFactory
    {
        /// <summary>Entra scope for Azure SQL Database and Elastic Pool.</summary>
        internal const string AzureSqlTokenScope = "https://database.windows.net/.default";

        /// <summary>Entra scope for PostgreSQL and MySQL Flexible Server.</summary>
        internal const string OssRdbmsTokenScope = "https://ossrdbms-aad.database.windows.net/.default";

        /// <summary>Command timeout used when the caller passes default or zero.</summary>
        internal static readonly TimeSpan DefaultCommandTimeout = TimeSpan.FromSeconds(30);

        private readonly ISqlQueryRowReader rowReader;

        /// <summary>Initializes a new instance of the <see cref="SqlQuerySessionFactory"/> class with the ADO reader.</summary>
        public SqlQuerySessionFactory()
            : this(new AdoSqlQueryRowReader())
        {
        }

        /// <summary>Initializes a new instance of the <see cref="SqlQuerySessionFactory"/> class.</summary>
        /// <param name="rowReader">Reader that executes SQL. Tests pass an in-memory reader.</param>
        public SqlQuerySessionFactory(ISqlQueryRowReader rowReader)
        {
            this.rowReader = rowReader;
        }

        /// <summary>
        /// Opens a read session and returns the first row. Throws when host, type, or (for OSS) Entra user name cannot be resolved.
        /// </summary>
        /// <param name="state">Refreshed resource state.</param>
        /// <param name="credential">Process TokenCredential used for ARM and Azure Monitor.</param>
        /// <param name="query">Operator SQL, executed as configured.</param>
        /// <param name="commandTimeout">Command timeout; 30 seconds when default or zero.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <param name="connectionAttributes">Optional QueryConnection extras merged onto the session string.</param>
        /// <returns>First-row columns, or empty when the Query returns no rows.</returns>
        public async Task<IReadOnlyList<SqlQueryColumn>> ReadFirstRowAsync(
            ResourceState state,
            TokenCredential credential,
            string query,
            TimeSpan commandTimeout,
            CancellationToken cancellationToken,
            IReadOnlyDictionary<string, string>? connectionAttributes = null)
        {
            var plan = this.CreateConnectionPlan(state, commandTimeout, connectionAttributes);
            var token = await credential.GetTokenAsync(new TokenRequestContext(new[] { plan.TokenScope }), cancellationToken);
            var entraUserName = NeedsEntraUserName(plan.Engine) ? EntraTokenUserName.TryRead(token.Token) : null;
            if (NeedsEntraUserName(plan.Engine) && string.IsNullOrWhiteSpace(entraUserName))
            {
                throw new InvalidOperationException(
                    "Query skipped: the access token has no Entra user name (preferred_username, upn, unique_name, or appid).");
            }

            return await this.rowReader.ReadFirstRowAsync(plan, token.Token, entraUserName, query, cancellationToken);
        }

        /// <summary>
        /// Derives host, catalog, engine, and connection string from the refreshed resource. Does not open a session.
        /// </summary>
        /// <param name="state">Resource state after Refresh, with FQDN populated for SQL types.</param>
        /// <param name="commandTimeout">Command timeout; 30 seconds when default or zero.</param>
        /// <param name="connectionAttributes">QueryConnection extras. Azure SQL validation requires ApplicationIntent.</param>
        /// <returns>Connection plan for the Query session.</returns>
        public SqlQueryConnectionPlan CreateConnectionPlan(
            ResourceState state,
            TimeSpan commandTimeout,
            IReadOnlyDictionary<string, string>? connectionAttributes = null)
        {
            var timeout = commandTimeout <= TimeSpan.Zero ? DefaultCommandTimeout : commandTimeout;
            var resourceId = state.AzureResourceId;
            var host = this.ReadFullyQualifiedDomainName(state);
            if (string.IsNullOrWhiteSpace(host))
            {
                throw new InvalidOperationException($"Query skipped: SQL host FQDN is missing after Refresh for {resourceId}.");
            }

            if (ResourceStateFactory.SqlDatabase.IsMatch(resourceId))
            {
                if (!state.ResourceParts.TryGetValue("databaseName", out var databaseName) || string.IsNullOrWhiteSpace(databaseName))
                {
                    throw new InvalidOperationException($"Query skipped: Azure SQL Database catalog is missing from ResourceId {resourceId}.");
                }

                return this.CreateAzureSqlPlan(host, databaseName, timeout, connectionAttributes);
            }

            if (ResourceStateFactory.ElasticPools.IsMatch(resourceId))
            {
                return this.CreateAzureSqlPlan(host, "master", timeout, connectionAttributes);
            }

            if (ResourceStateFactory.PostgreSqlFlexibleServer.IsMatch(resourceId))
            {
                return this.CreateOssPlan(SqlQueryEngine.PostgreSql, host, "postgres", timeout, connectionAttributes);
            }

            if (ResourceStateFactory.MySqlFlexibleServer.IsMatch(resourceId))
            {
                return this.CreateOssPlan(SqlQueryEngine.MySql, host, "mysql", timeout, connectionAttributes);
            }

            throw new InvalidOperationException($"Query skipped: resource {resourceId} is not a supported SQL type.");
        }

        /// <summary>True when the engine login is an Entra user name from the access token (PostgreSQL and MySQL).</summary>
        /// <param name="engine">Resolved Query engine.</param>
        /// <returns>True for PostgreSQL and MySQL Flexible Server.</returns>
        private static bool NeedsEntraUserName(SqlQueryEngine engine)
        {
            return engine == SqlQueryEngine.PostgreSql || engine == SqlQueryEngine.MySql;
        }

        /// <summary>Reads the ARM FQDN stored on the four SQL resource states.</summary>
        /// <param name="state">Refreshed resource state.</param>
        /// <returns>FQDN, or null when the state is not a SQL type or Refresh did not set it.</returns>
        private string? ReadFullyQualifiedDomainName(ResourceState state)
        {
            return state switch
            {
                MsSqlDatabaseResourceState sqlDatabase => sqlDatabase.FullyQualifiedDomainName,
                MssqlElasticPoolResourceState elasticPool => elasticPool.FullyQualifiedDomainName,
                PostgreSqlFlexibleServerResourceState postgreSql => postgreSql.FullyQualifiedDomainName,
                MySqlFlexibleServerResourceState mySql => mySql.FullyQualifiedDomainName,
                _ => null,
            };
        }

        /// <summary>Builds an Azure SQL plan. ApplicationIntent is taken only from QueryConnection.</summary>
        /// <param name="host">Logical-server FQDN.</param>
        /// <param name="catalog">Database name or master.</param>
        /// <param name="timeout">Command timeout.</param>
        /// <param name="connectionAttributes">Operator QueryConnection extras.</param>
        /// <returns>Azure SQL connection plan.</returns>
        private SqlQueryConnectionPlan CreateAzureSqlPlan(
            string host,
            string catalog,
            TimeSpan timeout,
            IReadOnlyDictionary<string, string>? connectionAttributes)
        {
            return new SqlQueryConnectionPlan(
                SqlQueryEngine.AzureSql,
                host,
                catalog,
                SqlQueryConnectionAttributes.BuildAzureSql(host, catalog, connectionAttributes),
                AzureSqlTokenScope,
                timeout);
        }

        /// <summary>Builds a PostgreSQL or MySQL plan with the OSS RDBMS token scope and optional extras.</summary>
        /// <param name="engine">PostgreSQL or MySQL.</param>
        /// <param name="host">Flexible-server FQDN.</param>
        /// <param name="catalog">Implied engine catalog.</param>
        /// <param name="timeout">Command timeout.</param>
        /// <param name="connectionAttributes">Operator QueryConnection extras.</param>
        /// <returns>OSS connection plan.</returns>
        private SqlQueryConnectionPlan CreateOssPlan(
            SqlQueryEngine engine,
            string host,
            string catalog,
            TimeSpan timeout,
            IReadOnlyDictionary<string, string>? connectionAttributes)
        {
            var connectionString = engine == SqlQueryEngine.PostgreSql
                ? $"Host={host};Database={catalog};SSL Mode=VerifyFull"
                : $"Server={host};Database={catalog};SslMode=VerifyFull";
            return new SqlQueryConnectionPlan(
                engine,
                host,
                catalog,
                SqlQueryConnectionAttributes.MergeOss(connectionString, connectionAttributes),
                OssRdbmsTokenScope,
                timeout);
        }
    }
}
