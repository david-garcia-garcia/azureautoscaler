using Azure.ResourceManager;
using Microsoft.Extensions.Logging;
using Moq;
using poolautoscaler.configuration;
using poolautoscaler.resourcemanagement;
using poolautoscaler.resourcemanagement.Dto;

namespace poolautoscaler.tests
{
    /// <summary>
    /// Tests for ResourceManager centralized filtering (tasks 7.2, 7.3, 7.4).
    /// </summary>
    public class ResourceManagerTests
    {
        private readonly Mock<ILoggerFactory> logFactoryMock;
        private readonly Mock<ILogger> loggerMock;
        private readonly Mock<IResourceStateFactory> factoryMock;
        private readonly Mock<ArmClient> armClientMock;

        public ResourceManagerTests()
        {
            this.loggerMock = new Mock<ILogger>();
            this.logFactoryMock = new Mock<ILoggerFactory>();
            this.logFactoryMock.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(this.loggerMock.Object);
            this.factoryMock = new Mock<IResourceStateFactory>();
            this.armClientMock = new Mock<ArmClient>();
        }

        [Fact]
        public async Task DiscoverAsync_WithFilter_OnlyPassingResourcesAreCreated()
        {
            var dtuId = "/subs/s/rg/r/srv/databases/db-dtu";
            var vcoreId = "/subs/s/rg/r/srv/databases/db-vcore";
            var poolId = "/subs/s/rg/r/srv/databases/db-pool";

            this.factoryMock
                .Setup(f => f.ExpandResourcesAsync(It.IsAny<ArmClient>(), "db", It.IsAny<string>(), It.IsAny<ILogger>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<string, ExpandedResource>
                {
                    ["db_db-dtu"] = MakeExpanded(dtuId, "db-dtu"),
                    ["db_db-vcore"] = MakeExpanded(vcoreId, "db-vcore"),
                    ["db_db-pool"] = MakeExpanded(poolId, "db-pool"),
                });

            this.factoryMock
                .Setup(f => f.Create(dtuId, It.IsAny<ILogger>(), It.IsAny<Resource>(), It.IsAny<ResourceInstance>()))
                .Returns(MakeState(dtuId));

            var resource = BuildResource("db", "/subs/s/rg/r/srv/databases/*", filter: ctx => ctx.ResourceName == "db-dtu");
            var manager = new ResourceManager(this.logFactoryMock.Object, this.factoryMock.Object);

            await manager.DiscoverAsync(this.armClientMock.Object, new[] { resource }, TimeSpan.Zero);

            this.factoryMock.Verify(
                f => f.Create(dtuId, It.IsAny<ILogger>(), It.IsAny<Resource>(), It.IsAny<ResourceInstance>()),
                Times.Once);
            this.factoryMock.Verify(
                f => f.Create(vcoreId, It.IsAny<ILogger>(), It.IsAny<Resource>(), It.IsAny<ResourceInstance>()),
                Times.Never);
            this.factoryMock.Verify(
                f => f.Create(poolId, It.IsAny<ILogger>(), It.IsAny<Resource>(), It.IsAny<ResourceInstance>()),
                Times.Never);
            Assert.Single(manager.Resources);
            Assert.True(manager.Resources.ContainsKey("db_db-dtu"));
        }

        [Fact]
        public async Task DiscoverAsync_WithNoFilter_AllExpandedResourcesAreCreated()
        {
            var id1 = "/subs/s/rg/r/srv/databases/db1";
            var id2 = "/subs/s/rg/r/srv/databases/db2";

            this.factoryMock
                .Setup(f => f.ExpandResourcesAsync(It.IsAny<ArmClient>(), "db", It.IsAny<string>(), It.IsAny<ILogger>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<string, ExpandedResource>
                {
                    ["db_db1"] = MakeExpanded(id1, "db1"),
                    ["db_db2"] = MakeExpanded(id2, "db2"),
                });

            this.factoryMock
                .Setup(f => f.Create(It.IsAny<string>(), It.IsAny<ILogger>(), It.IsAny<Resource>(), It.IsAny<ResourceInstance>()))
                .Returns<string, ILogger, Resource, ResourceInstance?>((rid, l, r, ri) => MakeState(rid));

            var resource = BuildResource("db", "/subs/s/rg/r/srv/databases/*", filter: null);
            var manager = new ResourceManager(this.logFactoryMock.Object, this.factoryMock.Object);

            await manager.DiscoverAsync(this.armClientMock.Object, new[] { resource }, TimeSpan.Zero);

            this.factoryMock.Verify(f => f.Create(id1, It.IsAny<ILogger>(), It.IsAny<Resource>(), It.IsAny<ResourceInstance>()), Times.Once);
            this.factoryMock.Verify(f => f.Create(id2, It.IsAny<ILogger>(), It.IsAny<Resource>(), It.IsAny<ResourceInstance>()), Times.Once);
            Assert.Equal(2, manager.Resources.Count);
        }

        [Fact]
        public async Task DiscoverAsync_WithFilterButNullContext_ResourceIsAlwaysCreated()
        {
            var literalId = "/subs/s/rg/r/srv/databases/mydb";

            this.factoryMock
                .Setup(f => f.ExpandResourcesAsync(It.IsAny<ArmClient>(), "db", It.IsAny<string>(), It.IsAny<ILogger>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<string, ExpandedResource>
                {
                    ["db"] = MakeExpanded(literalId, hasContext: false),
                });

            this.factoryMock
                .Setup(f => f.Create(literalId, It.IsAny<ILogger>(), It.IsAny<Resource>(), It.IsAny<ResourceInstance>()))
                .Returns(MakeState(literalId));

            var resource = BuildResource("db", literalId, filter: _ => false);
            var manager = new ResourceManager(this.logFactoryMock.Object, this.factoryMock.Object);

            await manager.DiscoverAsync(this.armClientMock.Object, new[] { resource }, TimeSpan.Zero);

            this.factoryMock.Verify(
                f => f.Create(literalId, It.IsAny<ILogger>(), It.IsAny<Resource>(), It.IsAny<ResourceInstance>()),
                Times.Once);
            Assert.Single(manager.Resources);
        }

        [Fact]
        public async Task DiscoverAsync_WithFilter_CorrectNumberOfResourcesAreCreated()
        {
            var allIds = Enumerable.Range(1, 5)
                .Select(i => (Key: $"db_{i}", Id: $"/subs/s/rg/r/srv/databases/db{i}", Name: $"db_{i}"))
                .ToList();

            this.factoryMock
                .Setup(f => f.ExpandResourcesAsync(It.IsAny<ArmClient>(), "db", It.IsAny<string>(), It.IsAny<ILogger>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(allIds.ToDictionary(t => t.Key, t => MakeExpanded(t.Id, t.Name)));

            this.factoryMock
                .Setup(f => f.Create(It.IsAny<string>(), It.IsAny<ILogger>(), It.IsAny<Resource>(), It.IsAny<ResourceInstance>()))
                .Returns<string, ILogger, Resource, ResourceInstance?>((rid, l, r, ri) => MakeState(rid));

            var resource = BuildResource(
                "db",
                "/subs/s/rg/r/srv/databases/*",
                filter: ctx => ctx.ResourceName is "db_1" or "db_2" or "db_3");

            var manager = new ResourceManager(this.logFactoryMock.Object, this.factoryMock.Object);

            await manager.DiscoverAsync(this.armClientMock.Object, new[] { resource }, TimeSpan.Zero);

            Assert.Equal(3, manager.Resources.Count);
            Assert.True(manager.Resources.ContainsKey("db_1"));
            Assert.True(manager.Resources.ContainsKey("db_2"));
            Assert.True(manager.Resources.ContainsKey("db_3"));
            Assert.False(manager.Resources.ContainsKey("db_4"));
            Assert.False(manager.Resources.ContainsKey("db_5"));
        }

        private static ResourceState MakeState(string resourceId)
        {
            return new TestResourceState(resourceId, new Mock<ILogger>().Object, new Resource());
        }

        private static ExpandedResource MakeExpanded(string resourceId, string? name = null, bool hasContext = true)
        {
            ResourceFilterContext? ctx = hasContext
                ? new ResourceFilterContext
                {
                    ResourceName = name ?? resourceId,
                    Tags = new Dictionary<string, string>(),
                    Resource = new object(),
                }
                : null;

            return new ExpandedResource { ResourceId = resourceId, Context = ctx };
        }

        private static Resource BuildResource(
            string instanceKey,
            string resourceId,
            Func<ResourceFilterContext, bool>? filter = null)
        {
            return new Resource
            {
                Enabled = true,
                Frequency = "4m",
                FrequencyParsed = TimeSpan.FromMinutes(4),
                Resources = new Dictionary<string, ResourceInstance>
                {
                    [instanceKey] = new ResourceInstance
                    {
                        Id = instanceKey,
                        ResourceId = resourceId,
                        ResourceFilterExpression = filter,
                    },
                },
            };
        }
    }
}
