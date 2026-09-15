using Azure.Core;
using Azure.ResourceManager;
using Microsoft.Extensions.Logging;
using Moq;
using poolautoscaler.configuration;
using poolautoscaler.metrics;
using poolautoscaler.resourcemanagement;
using poolautoscaler.resources.MsSqlDatabase;

namespace poolautoscaler.tests
{
    /// <summary>Query path on CustomMetricsPusher: one run, numeric POSTs, skip, and per-row catch.</summary>
    public class CustomMetricsPusherQueryTests
    {
        private readonly ILogger logger = new Mock<ILogger>().Object;
        private readonly Mock<TokenCredential> credential = new();
        private readonly Mock<ArmClient> armClient = new();
        private readonly Mock<IResourceLocationResolver> locationResolver = new();

        public CustomMetricsPusherQueryTests()
        {
            this.credential
                .Setup(c => c.GetTokenAsync(It.IsAny<TokenRequestContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AccessToken("not-a-jwt", DateTimeOffset.UtcNow.AddHours(1)));
            this.locationResolver
                .Setup(r => r.GetRegionAsync(It.IsAny<ArmClient>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("westeurope");
        }

        [Fact]
        public async Task PushIfDueAsync_OneQueryRun_PostsEachNumericColumn()
        {
            var rowReader = new TestSqlQueryRowReader(
                new[]
                {
                    new SqlQueryColumn("cpu_percent", 10.0d, typeof(double)),
                    new SqlQueryColumn("data_io_percent", 4.0d, typeof(double)),
                    new SqlQueryColumn("from_time", DateTime.UtcNow, typeof(DateTime)),
                });
            var http = new TestCapturingMetricsHttpHandler();
            var pusher = this.CreatePusher(rowReader, http);
            var state = this.CreateSqlState(
                new CustomMetricConfig { Query = "SELECT 1 AS cpu_percent", FrequencyParsed = TimeSpan.Zero });

            await pusher.PushIfDueAsync(state, CancellationToken.None);

            Assert.Equal(1, rowReader.ReadCount);
            Assert.Equal(2, http.Bodies.Count);
            Assert.Contains("cpu_percent", http.Bodies[0]);
            Assert.Contains("data_io_percent", http.Bodies[1]);
            Assert.DoesNotContain(http.Bodies, body => body.Contains("from_time"));
        }

        [Fact]
        public async Task PushIfDueAsync_EmptyOrNonNumeric_DoesNotPost()
        {
            var rowReader = new TestSqlQueryRowReader(
                new[] { new SqlQueryColumn("from_time", DateTime.UtcNow, typeof(DateTime)) });
            var http = new TestCapturingMetricsHttpHandler();
            var pusher = this.CreatePusher(rowReader, http);
            var state = this.CreateSqlState(
                new CustomMetricConfig { Query = "SELECT GETDATE() AS from_time", FrequencyParsed = TimeSpan.Zero });

            await pusher.PushIfDueAsync(state, CancellationToken.None);

            Assert.Equal(1, rowReader.ReadCount);
            Assert.Empty(http.Bodies);
        }

        [Fact]
        public async Task PushIfDueAsync_FactoryThrow_ContinuesOtherCustomMetrics()
        {
            var rowReader = new TestSqlQueryRowReader(
                new[] { new SqlQueryColumn("sku_cores", 2.0d, typeof(double)) },
                query => query.Contains("fail", StringComparison.Ordinal) ? new InvalidOperationException("query failed") : null);
            var http = new TestCapturingMetricsHttpHandler();
            var pusher = this.CreatePusher(rowReader, http);
            var state = this.CreateSqlState(
                new CustomMetricConfig { Query = "SELECT fail", FrequencyParsed = TimeSpan.Zero },
                new CustomMetricConfig { Query = "SELECT 2 AS sku_cores", FrequencyParsed = TimeSpan.Zero });

            await pusher.PushIfDueAsync(state, CancellationToken.None);

            Assert.Equal(2, rowReader.ReadCount);
            Assert.Single(http.Bodies);
            Assert.Contains("sku_cores", http.Bodies[0]);
        }

        private CustomMetricsPusher CreatePusher(TestSqlQueryRowReader rowReader, TestCapturingMetricsHttpHandler http)
        {
            return new CustomMetricsPusher(
                this.credential.Object,
                this.armClient.Object,
                this.logger,
                this.locationResolver.Object,
                sqlQuerySessionFactory: new SqlQuerySessionFactory(rowReader),
                metricsHttpHandler: http);
        }

        private MsSqlDatabaseResourceState CreateSqlState(params CustomMetricConfig[] metrics)
        {
            var resourceId = "/subscriptions/sub/resourceGroups/rg/providers/Microsoft.Sql/servers/srv/databases/appdb";
            var state = new MsSqlDatabaseResourceState(
                resourceId,
                this.logger,
                new Resource { CustomMetrics = metrics.ToList() });
            state.FullyQualifiedDomainName = "sqlsrv.database.windows.net";
            state.ResourceParts["databaseName"] = "appdb";
            foreach (var metric in metrics)
            {
                if (string.IsNullOrEmpty(metric.ResourceId))
                {
                    metric.ResourceId = resourceId;
                }
            }

            return state;
        }
    }
}
