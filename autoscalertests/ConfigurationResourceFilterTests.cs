using Microsoft.Extensions.Logging;
using Moq;
using poolautoscaler.configuration;

namespace poolautoscaler.tests
{
    /// <summary>
    /// Tests for ResourceFilter compilation in Configuration.PrepareAndValidate (task 7.1).
    /// </summary>
    public class ConfigurationResourceFilterTests
    {
        private readonly ILogger logger = new Mock<ILogger>().Object;

        [Fact]
        public void PrepareAndValidate_ValidResourceFilter_CompilesWithoutError()
        {
            var config = BuildConfig("(r) => r.ResourceName != \"master\"");

            config.PrepareAndValidate(this.logger);

            var instance = config.Resources[0].Resources["db1"];
            Assert.NotNull(instance.ResourceFilterExpression);
        }

        [Fact]
        public void PrepareAndValidate_ValidResourceFilter_ExpressionIsCallable()
        {
            var config = BuildConfig("(r) => r.ResourceName != \"master\"");
            config.PrepareAndValidate(this.logger);

            var expr = config.Resources[0].Resources["db1"].ResourceFilterExpression!;
            var ctx = new ResourceFilterContext { ResourceName = "mydb", Tags = new Dictionary<string, string>(), Resource = new object() };

            Assert.True(expr(ctx));
        }

        [Fact]
        public void PrepareAndValidate_InvalidResourceFilter_ThrowsDescriptiveError()
        {
            var config = BuildConfig("(r) => r.DoesNotExist");

            var ex = Assert.Throws<Exception>(() => config.PrepareAndValidate(this.logger));
            Assert.Contains("r.DoesNotExist", ex.Message);
        }

        [Fact]
        public void PrepareAndValidate_NullResourceFilter_LeavesExpressionNull()
        {
            var config = BuildConfig(null);

            config.PrepareAndValidate(this.logger);

            var instance = config.Resources[0].Resources["db1"];
            Assert.Null(instance.ResourceFilterExpression);
        }

        [Fact]
        public void PrepareAndValidate_EmptyResourceFilter_LeavesExpressionNull()
        {
            var config = BuildConfig(string.Empty);

            config.PrepareAndValidate(this.logger);

            var instance = config.Resources[0].Resources["db1"];
            Assert.Null(instance.ResourceFilterExpression);
        }

        private static Configuration BuildConfig(string? resourceFilter)
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
                            ["db1"] = new ResourceInstance
                            {
                                Id = "db1",
                                ResourceId = "/subscriptions/sub/resourceGroups/rg/providers/Microsoft.Sql/servers/srv/databases/mydb",
                                ResourceFilter = resourceFilter,
                            },
                        },
                    },
                ],
            };
        }
    }
}
