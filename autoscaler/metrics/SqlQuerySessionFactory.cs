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
        /// <returns>First-row columns, or empty when the Query returns no rows.</returns>
        public async Task<IReadOnlyList<SqlQueryColumn>> ReadFirstRowAsync(
            ResourceState state,
            TokenCredential credential,
            string query,
            TimeSpan commandTimeout,
            CancellationToken cancellationToken)
        {
            var plan = this.CreateConnectionPlan(state, commandTimeout);
            var token = await credential.GetTokenAsync(new TokenRequestContext(new[] { plan.TokenScope }), cancellationToken);
            var entraUserName = plan.Engine == SqlQueryEngine.AzureSql ? null : EntraTokenUserName.TryRead(token.Token);
            if (plan.Engine != SqlQueryEngine.AzureSql && string.IsNullOrWhiteSpace(entraUserName))
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
        /// <returns>Connection plan for the Query session.</returns>
        public SqlQueryConnectionPlan CreateConnectionPlan(ResourceState state, TimeSpan commandTimeout)
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

                return this.CreateAzureSqlPlan(host, databaseName, timeout);
            }

            if (ResourceStateFactory.ElasticPools.IsMatch(resourceId))
            {
                return this.CreateAzureSqlPlan(host, "master", timeout);
            }

            if (ResourceStateFactory.PostgreSqlFlexibleServer.IsMatch(resourceId))
            {
                return this.CreateOssPlan(SqlQueryEngine.PostgreSql, host, "postgres", timeout);
            }

            if (ResourceStateFactory.MySqlFlexibleServer.IsMatch(resourceId))
            {
                return this.CreateOssPlan(SqlQueryEngine.MySql, host, "mysql", timeout);
            }

            throw new InvalidOperationException($"Query skipped: resource {resourceId} is not a supported SQL type.");
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

        /// <summary>Builds an Azure SQL plan with ApplicationIntent=ReadOnly and the database.windows.net token scope.</summary>
        /// <param name="host">Logical-server FQDN.</param>
        /// <param name="catalog">Database name or master.</param>
        /// <param name="timeout">Command timeout.</param>
        /// <returns>Azure SQL connection plan.</returns>
        private SqlQueryConnectionPlan CreateAzureSqlPlan(string host, string catalog, TimeSpan timeout)
        {
            var connectionString =
                $"Server=tcp:{host},1433;Initial Catalog={catalog};Encrypt=True;TrustServerCertificate=False;ApplicationIntent=ReadOnly";
            return new SqlQueryConnectionPlan(
                SqlQueryEngine.AzureSql,
                host,
                catalog,
                connectionString,
                AzureSqlTokenScope,
                timeout);
        }

        /// <summary>Builds a PostgreSQL or MySQL plan with the OSS RDBMS token scope and no ApplicationIntent keyword.</summary>
        /// <param name="engine">PostgreSQL or MySQL.</param>
        /// <param name="host">Flexible-server FQDN.</param>
        /// <param name="catalog">Implied engine catalog.</param>
        /// <param name="timeout">Command timeout.</param>
        /// <returns>OSS connection plan.</returns>
        private SqlQueryConnectionPlan CreateOssPlan(SqlQueryEngine engine, string host, string catalog, TimeSpan timeout)
        {
            var connectionString = engine == SqlQueryEngine.PostgreSql
                ? $"Host={host};Database={catalog};SSL Mode=Require"
                : $"Server={host};Database={catalog};SslMode=Required";
            return new SqlQueryConnectionPlan(
                engine,
                host,
                catalog,
                connectionString,
                OssRdbmsTokenScope,
                timeout);
        }
    }
}
