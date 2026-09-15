using Azure.Core;
using Azure.ResourceManager;
using Microsoft.Extensions.Logging;
using Moq;
using poolautoscaler.configuration;
using poolautoscaler.resources.MsSqlDatabase;
using poolautoscaler.resources.MssqlElasticPool;
using poolautoscaler.resources.MySqlFlexibleServer;
using poolautoscaler.resources.PostgreSqlFlexibleServer;

namespace poolautoscaler.tests
{
    /// <summary>QUERY does not implement in-process SQL CustomMetric gather.</summary>
    public class SqlResourceCustomMetricNotImplementedTests
    {
        private readonly ILogger logger = new Mock<ILogger>().Object;
        private readonly Mock<ArmClient> armClient = new();
        private readonly Mock<TokenCredential> credential = new();

        [Fact]
        public async Task CustomMetric_MsSqlDatabase_StillThrowsNotImplemented()
        {
            var state = new MsSqlDatabaseResourceState(
                "/subscriptions/sub/resourceGroups/rg/providers/Microsoft.Sql/servers/srv/databases/db",
                this.logger,
                new Resource());

            await Assert.ThrowsAsync<NotImplementedException>(() =>
                state.CustomMetric(this.armClient.Object, this.credential.Object, CancellationToken.None, new ScalingConfiguration(), "custom_dtu"));
        }

        [Fact]
        public async Task CustomMetric_ElasticPool_StillThrowsNotImplemented()
        {
            var state = new MssqlElasticPoolResourceState(
                "/subscriptions/sub/resourceGroups/rg/providers/Microsoft.Sql/servers/srv/elasticPools/pool",
                this.logger,
                new Resource());

            await Assert.ThrowsAsync<NotImplementedException>(() =>
                state.CustomMetric(this.armClient.Object, this.credential.Object, CancellationToken.None, new ScalingConfiguration(), "custom_dtu"));
        }

        [Fact]
        public async Task CustomMetric_PostgreSql_StillThrowsNotImplemented()
        {
            var state = new PostgreSqlFlexibleServerResourceState(
                "/subscriptions/sub/resourceGroups/rg/providers/Microsoft.DBforPostgreSQL/flexibleServers/pg",
                this.logger,
                new Resource());

            await Assert.ThrowsAsync<NotImplementedException>(() =>
                state.CustomMetric(this.armClient.Object, this.credential.Object, CancellationToken.None, new ScalingConfiguration(), "custom_dtu"));
        }

        [Fact]
        public async Task CustomMetric_MySql_StillThrowsNotImplemented()
        {
            var state = new MySqlFlexibleServerResourceState(
                "/subscriptions/sub/resourceGroups/rg/providers/Microsoft.DBforMySQL/flexibleServers/my",
                this.logger,
                new Resource());

            await Assert.ThrowsAsync<NotImplementedException>(() =>
                state.CustomMetric(this.armClient.Object, this.credential.Object, CancellationToken.None, new ScalingConfiguration(), "custom_dtu"));
        }
    }
}
