using Microsoft.Extensions.Logging;
using Moq;
using poolautoscaler.configuration;

namespace poolautoscaler.tests
{
    /// <summary>Startup validation for Query XOR DataExpression on CustomMetrics.</summary>
    public class ConfigurationCustomMetricQueryTests
    {
        private const string SqlDatabaseId =
            "/subscriptions/sub/resourceGroups/rg/providers/Microsoft.Sql/servers/srv/databases/mydb";

        private readonly ILogger logger = new Mock<ILogger>().Object;

        [Fact]
        public void PrepareAndValidate_DataExpressionWithoutName_Throws()
        {
            var config = this.BuildConfig(
                resourceId: SqlDatabaseId,
                metric: new CustomMetricConfig { DataExpression = "(data) => Convert.ToDouble(1)" });

            var ex = Assert.Throws<Exception>(() => config.PrepareAndValidate(this.logger));
            Assert.Contains("Name", ex.Message);
        }

        [Fact]
        public void PrepareAndValidate_DataExpressionWithName_CompilesDelegate()
        {
            var config = this.BuildConfig(
                resourceId: SqlDatabaseId,
                metric: new CustomMetricConfig
                {
                    Name = "core_count",
                    DataExpression = "(data) => Convert.ToDouble(2)",
                });

            config.PrepareAndValidate(this.logger);

            Assert.NotNull(config.Resources[0].CustomMetrics[0].DataExpressionDelegate);
        }

        [Fact]
        public void PrepareAndValidate_QueryWithoutName_Accepts()
        {
            var config = this.BuildConfig(
                resourceId: SqlDatabaseId,
                metric: new CustomMetricConfig { Query = "SELECT 1 AS cpu_percent" });

            var ex = Record.Exception(() => config.PrepareAndValidate(this.logger));

            Assert.Null(ex);
            Assert.Null(config.Resources[0].CustomMetrics[0].DataExpressionDelegate);
            Assert.Equal(TimeSpan.FromSeconds(30), config.Resources[0].CustomMetrics[0].QueryTimeoutParsed);
        }

        [Fact]
        public void PrepareAndValidate_BothSources_Throws()
        {
            var config = this.BuildConfig(
                resourceId: SqlDatabaseId,
                metric: new CustomMetricConfig
                {
                    Name = "x",
                    DataExpression = "(data) => Convert.ToDouble(1)",
                    Query = "SELECT 1 AS cpu_percent",
                });

            var ex = Assert.Throws<Exception>(() => config.PrepareAndValidate(this.logger));
            Assert.Contains("exactly one", ex.Message);
        }

        [Fact]
        public void PrepareAndValidate_NeitherSource_Throws()
        {
            var config = this.BuildConfig(
                resourceId: SqlDatabaseId,
                metric: new CustomMetricConfig { Name = "x" });

            var ex = Assert.Throws<Exception>(() => config.PrepareAndValidate(this.logger));
            Assert.Contains("Query or a DataExpression", ex.Message);
        }

        [Theory]
        [InlineData("/subscriptions/sub/resourceGroups/rg/providers/Microsoft.Sql/servers/srv/databases/db")]
        [InlineData("/subscriptions/sub/resourceGroups/rg/providers/Microsoft.Sql/servers/srv/elasticPools/pool")]
        [InlineData("/subscriptions/sub/resourceGroups/rg/providers/Microsoft.DBforPostgreSQL/flexibleServers/pg")]
        [InlineData("/subscriptions/sub/resourceGroups/rg/providers/Microsoft.DBforMySQL/flexibleServers/my")]
        public void PrepareAndValidate_QueryOnSqlResource_Accepts(string resourceId)
        {
            var config = this.BuildConfig(
                resourceId: resourceId,
                metric: new CustomMetricConfig { Query = "SELECT 1 AS cpu_percent" });

            var ex = Record.Exception(() => config.PrepareAndValidate(this.logger));
            Assert.Null(ex);
        }

        [Fact]
        public void PrepareAndValidate_QueryOnNonSqlResource_Throws()
        {
            var config = this.BuildConfig(
                resourceId: "/subscriptions/sub/resourceGroups/rg/providers/Microsoft.ContainerService/managedClusters/aks/agentPools/pool",
                metric: new CustomMetricConfig { Query = "SELECT 1 AS cpu_percent" });

            var ex = Assert.Throws<Exception>(() => config.PrepareAndValidate(this.logger));
            Assert.Contains("not allowed", ex.Message);
        }

        private Configuration BuildConfig(string resourceId, CustomMetricConfig metric)
        {
            return new Configuration
            {
                DefaultResourceFrequency = "4m",
                ResourceDiscoveryFrequency = "1h",
                Resources =
                [
                    new Resource
                    {
                        Frequency = "4m",
                        Resources = new Dictionary<string, ResourceInstance>
                        {
                            ["one"] = new ResourceInstance
                            {
                                Id = "one",
                                ResourceId = resourceId,
                            },
                        },
                        CustomMetrics = [metric],
                    },
                ],
            };
        }
    }
}
