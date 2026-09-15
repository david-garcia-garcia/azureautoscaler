using System.Data;
using Azure.Core;
using Microsoft.Extensions.Logging;
using Moq;
using poolautoscaler.configuration;
using poolautoscaler.metrics;
using poolautoscaler.resources.MsSqlDatabase;
using poolautoscaler.resources.MssqlElasticPool;
using poolautoscaler.resources.MySqlFlexibleServer;
using poolautoscaler.resources.PostgreSqlFlexibleServer;

namespace poolautoscaler.tests
{
    /// <summary>Connection-plan and first-row mapping for Query custom metrics. No live SQL.</summary>
    public class SqlQuerySessionFactoryTests
    {
        private readonly ILogger logger = new Mock<ILogger>().Object;

        [Fact]
        public void CreateConnectionPlan_SqlDatabase_UsesResourceIdCatalogAndReadOnly()
        {
            var state = this.CreateSqlDatabaseState();
            state.FullyQualifiedDomainName = "sqlsrv.database.windows.net";
            state.ResourceParts["databaseName"] = "appdb";
            var factory = new SqlQuerySessionFactory();

            var plan = factory.CreateConnectionPlan(state, TimeSpan.Zero);

            Assert.Equal(SqlQueryEngine.AzureSql, plan.Engine);
            Assert.Equal("appdb", plan.Catalog);
            Assert.Equal("sqlsrv.database.windows.net", plan.Host);
            Assert.Contains("ApplicationIntent=ReadOnly", plan.ConnectionString);
            Assert.Equal(SqlQuerySessionFactory.AzureSqlTokenScope, plan.TokenScope);
            Assert.Equal(TimeSpan.FromSeconds(30), plan.CommandTimeout);
        }

        [Fact]
        public void CreateConnectionPlan_ElasticPool_UsesMasterCatalog()
        {
            var state = this.CreateElasticPoolState();
            state.FullyQualifiedDomainName = "sqlsrv.database.windows.net";
            var factory = new SqlQuerySessionFactory();

            var plan = factory.CreateConnectionPlan(state, TimeSpan.FromSeconds(45));

            Assert.Equal("master", plan.Catalog);
            Assert.Contains("ApplicationIntent=ReadOnly", plan.ConnectionString);
            Assert.Equal(TimeSpan.FromSeconds(45), plan.CommandTimeout);
        }

        [Fact]
        public void CreateConnectionPlan_PostgreSql_UsesPostgresCatalogWithoutApplicationIntent()
        {
            var state = this.CreatePostgreSqlState();
            state.FullyQualifiedDomainName = "pg.postgres.database.azure.com";
            var factory = new SqlQuerySessionFactory();

            var plan = factory.CreateConnectionPlan(state, TimeSpan.FromSeconds(30));

            Assert.Equal("postgres", plan.Catalog);
            Assert.Equal(SqlQueryEngine.PostgreSql, plan.Engine);
            Assert.DoesNotContain("ApplicationIntent", plan.ConnectionString);
            Assert.Contains("SSL Mode=VerifyFull", plan.ConnectionString);
            Assert.Equal(SqlQuerySessionFactory.OssRdbmsTokenScope, plan.TokenScope);
        }

        [Fact]
        public void CreateConnectionPlan_MySql_UsesMysqlCatalogWithoutApplicationIntent()
        {
            var state = this.CreateMySqlState();
            state.FullyQualifiedDomainName = "my.mysql.database.azure.com";
            var factory = new SqlQuerySessionFactory();

            var plan = factory.CreateConnectionPlan(state, TimeSpan.FromSeconds(30));

            Assert.Equal("mysql", plan.Catalog);
            Assert.Equal(SqlQueryEngine.MySql, plan.Engine);
            Assert.DoesNotContain("ApplicationIntent", plan.ConnectionString);
            Assert.Contains("SslMode=VerifyFull", plan.ConnectionString);
        }

        [Fact]
        public void FirstRowMapper_ReadsOnlyFirstRow()
        {
            var table = new DataTable();
            table.Columns.Add("cpu_percent", typeof(double));
            table.Rows.Add(11.5);
            table.Rows.Add(99.0);

            using var reader = table.CreateDataReader();
            var columns = SqlQueryFirstRowMapper.ReadFirstRow(reader);

            Assert.Single(columns);
            Assert.Equal("cpu_percent", columns[0].Name);
            Assert.Equal(11.5, (double)columns[0].Value!);
        }

        [Fact]
        public void NumericColumns_PublishesNumericAndIgnoresNonNumeric()
        {
            var columns = new List<SqlQueryColumn>
            {
                new SqlQueryColumn("cpu_percent", 12.5d, typeof(double)),
                new SqlQueryColumn("data_io_percent", 3, typeof(int)),
                new SqlQueryColumn("from_time", DateTime.UtcNow, typeof(DateTime)),
                new SqlQueryColumn("label", "replica", typeof(string)),
                new SqlQueryColumn("flag", true, typeof(bool)),
                new SqlQueryColumn("empty_cpu", null, typeof(double)),
            };

            var published = SqlQueryNumericColumns.SelectPublished(columns);

            Assert.Equal(2, published.Count);
            Assert.Equal("cpu_percent", published[0].Name);
            Assert.Equal(12.5, published[0].Value);
            Assert.Equal("data_io_percent", published[1].Name);
            Assert.Equal(3, published[1].Value);
        }

        [Fact]
        public async Task ReadFirstRowAsync_UsesInjectedReaderOnce()
        {
            var state = this.CreateSqlDatabaseState();
            state.FullyQualifiedDomainName = "sqlsrv.database.windows.net";
            state.ResourceParts["databaseName"] = "appdb";
            var rowReader = new TestSqlQueryRowReader(
                new[] { new SqlQueryColumn("cpu_percent", 8.0d, typeof(double)) });
            var factory = new SqlQuerySessionFactory(rowReader);
            var credential = new Mock<TokenCredential>();
            credential
                .Setup(c => c.GetTokenAsync(It.IsAny<TokenRequestContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AccessToken("not-a-jwt", DateTimeOffset.UtcNow.AddHours(1)));

            var columns = await factory.ReadFirstRowAsync(
                state,
                credential.Object,
                "SELECT 1 AS cpu_percent",
                TimeSpan.FromSeconds(30),
                CancellationToken.None);

            Assert.Equal(1, rowReader.ReadCount);
            Assert.Equal("SELECT 1 AS cpu_percent", rowReader.LastQuery);
            Assert.Single(columns);
        }

        [Fact]
        public void CreateConnectionPlan_SqlDatabase_ThrowsWhenFqdnMissing()
        {
            var state = this.CreateSqlDatabaseState();
            state.ResourceParts["databaseName"] = "appdb";
            var factory = new SqlQuerySessionFactory();

            var ex = Assert.Throws<InvalidOperationException>(() => factory.CreateConnectionPlan(state, TimeSpan.FromSeconds(30)));

            Assert.Contains("FQDN is missing", ex.Message);
        }

        [Fact]
        public void CreateConnectionPlan_SqlDatabase_ThrowsWhenCatalogMissing()
        {
            var state = this.CreateSqlDatabaseState();
            state.FullyQualifiedDomainName = "sqlsrv.database.windows.net";
            state.ResourceParts.Remove("databaseName");
            var factory = new SqlQuerySessionFactory();

            var ex = Assert.Throws<InvalidOperationException>(() => factory.CreateConnectionPlan(state, TimeSpan.FromSeconds(30)));

            Assert.Contains("catalog is missing", ex.Message);
        }

        [Fact]
        public async Task ReadFirstRowAsync_PostgreSql_ThrowsWhenTokenHasNoEntraUserName()
        {
            var state = this.CreatePostgreSqlState();
            state.FullyQualifiedDomainName = "pg.postgres.database.azure.com";
            var factory = new SqlQuerySessionFactory(new TestSqlQueryRowReader(Array.Empty<SqlQueryColumn>()));
            var credential = new Mock<TokenCredential>();
            credential
                .Setup(c => c.GetTokenAsync(It.IsAny<TokenRequestContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AccessToken(JwtWithoutEntraUserClaims(), DateTimeOffset.UtcNow.AddHours(1)));

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => factory.ReadFirstRowAsync(
                state,
                credential.Object,
                "SELECT 1",
                TimeSpan.FromSeconds(30),
                CancellationToken.None));

            Assert.Contains("no Entra user name", ex.Message);
        }

        /// <summary>Minimal JWT with no preferred_username, upn, unique_name, or appid.</summary>
        private static string JwtWithoutEntraUserClaims()
        {
            static string Base64Url(string json)
            {
                return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json))
                    .TrimEnd('=')
                    .Replace('+', '-')
                    .Replace('/', '_');
            }

            return $"{Base64Url("{\"alg\":\"none\"}")}.{Base64Url("{\"sub\":\"x\"}")}.";
        }

        private MsSqlDatabaseResourceState CreateSqlDatabaseState()
        {
            return new MsSqlDatabaseResourceState(
                "/subscriptions/sub/resourceGroups/rg/providers/Microsoft.Sql/servers/srv/databases/appdb",
                this.logger,
                new Resource());
        }

        private MssqlElasticPoolResourceState CreateElasticPoolState()
        {
            return new MssqlElasticPoolResourceState(
                "/subscriptions/sub/resourceGroups/rg/providers/Microsoft.Sql/servers/srv/elasticPools/pool",
                this.logger,
                new Resource());
        }

        private PostgreSqlFlexibleServerResourceState CreatePostgreSqlState()
        {
            return new PostgreSqlFlexibleServerResourceState(
                "/subscriptions/sub/resourceGroups/rg/providers/Microsoft.DBforPostgreSQL/flexibleServers/pg",
                this.logger,
                new Resource());
        }

        private MySqlFlexibleServerResourceState CreateMySqlState()
        {
            return new MySqlFlexibleServerResourceState(
                "/subscriptions/sub/resourceGroups/rg/providers/Microsoft.DBforMySQL/flexibleServers/my",
                this.logger,
                new Resource());
        }
    }
}
