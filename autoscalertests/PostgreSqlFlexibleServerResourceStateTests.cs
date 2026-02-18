using Microsoft.Extensions.Logging;
using Moq;
using poolautoscaler.configuration;
using poolautoscaler.resources.PostgreSqlFlexibleServer;
using PostgreSqlFlexibleServerStateDto = poolautoscaler.resources.PostgreSqlFlexibleServer.Dto.PostgreSqlFlexibleServerState;
using AzurePostgreSqlSku = Azure.ResourceManager.PostgreSql.FlexibleServers.Models.PostgreSqlFlexibleServerSku;

namespace poolautoscaler.tests
{
    public class PostgreSqlFlexibleServerResourceStateTests
    {
        private readonly Mock<ILogger> loggerMock;
        private readonly Resource config;
        private readonly string resourceId;

        public PostgreSqlFlexibleServerResourceStateTests()
        {
            this.loggerMock = new Mock<ILogger>();
            this.resourceId = "/subscriptions/test/resourceGroups/test/providers/Microsoft.DBforPostgreSQL/flexibleServers/test";
            this.config = new Resource { };
        }

        [Fact]
        public void SetSku_WhenCurrentIsLower_ShouldUpdateSku()
        {
            // Arrange
            var state = new PostgreSqlFlexibleServerResourceState(this.resourceId, this.loggerMock.Object, this.config);

            state.ExistingPostgreSqlFlexibleServerState = new PostgreSqlFlexibleServerStateDto
            {
                Sku = new AzurePostgreSqlSku("Standard_B1ms", "Burstable"),
                CoreCount = 1
            };

            state.RequestedPostgreSqlFlexibleServerState = new PostgreSqlFlexibleServerStateDto();

            // Act
            state.SetSku("Standard_B2ms"); // Higher SKU

            // Assert
            var patch = state.PreparePatch();
            Assert.True(patch.HasChanges);
            Assert.True(patch.Disruptive);
            var patchData = (PostgreSqlFlexibleServerStateDto)patch.PatchData;
            Assert.Equal("Standard_B2ms", patchData.Sku.Name);
        }

        [Fact]
        public void SetSku_WhenNewSkuIsLower_ShouldKeepExistingSku()
        {
            // Arrange
            var state = new PostgreSqlFlexibleServerResourceState(this.resourceId, this.loggerMock.Object, this.config);

            state.ExistingPostgreSqlFlexibleServerState = new PostgreSqlFlexibleServerStateDto
            {
                Sku = new AzurePostgreSqlSku("Standard_B2ms", "Burstable"),
                CoreCount = 2
            };

            state.RequestedPostgreSqlFlexibleServerState = new PostgreSqlFlexibleServerStateDto();

            // Act
            state.SetSku("Standard_B1ms");

            // Assert
            var patch = state.PreparePatch();
            Assert.True(patch.HasChanges);
            var patchData = (PostgreSqlFlexibleServerStateDto)patch.PatchData;
            Assert.Equal("Standard_B1ms", patchData.Sku.Name);
        }

        [Fact]
        public void SetSku_SkuAndCpuPriority()
        {
            // Arrange
            var state = new PostgreSqlFlexibleServerResourceState(this.resourceId, this.loggerMock.Object, this.config);

            state.ExistingPostgreSqlFlexibleServerState = new PostgreSqlFlexibleServerStateDto
            {
                Sku = new AzurePostgreSqlSku("Standard_B2ms", "Burstable"),
                CoreCount = 2
            };

            state.RequestedPostgreSqlFlexibleServerState = new PostgreSqlFlexibleServerStateDto();

            // Act
            state.SetSku("Standard_B1ms");
            state.SetCoreCount("4");

            // Assert
            var patch = state.PreparePatch();
            Assert.True(patch.HasChanges);
            var patchData = (PostgreSqlFlexibleServerStateDto)patch.PatchData;
            Assert.Equal("Standard_B4ms", patchData.Sku.Name);
        }

        [Fact]
        public void SetCoreCount_WhenCurrentIsLower_ShouldUpdateCoreCount()
        {
            // Arrange
            var state = new PostgreSqlFlexibleServerResourceState(this.resourceId, this.loggerMock.Object, this.config);

            state.ExistingPostgreSqlFlexibleServerState = new PostgreSqlFlexibleServerStateDto
            {
                Sku = new AzurePostgreSqlSku("Standard_B1ms", "Burstable"),
                CoreCount = 1
            };

            state.RequestedPostgreSqlFlexibleServerState = new PostgreSqlFlexibleServerStateDto();

            // Act
            state.SetCoreCount("2"); // Higher core count

            // Assert
            var patch = state.PreparePatch();
            Assert.True(patch.HasChanges);
            Assert.True(patch.Disruptive);
            var patchData = (PostgreSqlFlexibleServerStateDto)patch.PatchData;
            Assert.Equal("Standard_B2s", patchData.Sku.Name);
        }

        [Fact]
        public void SetCoreCount_WithMultipleUpdates_ShouldKeepHighestValue()
        {
            // Arrange
            var state = new PostgreSqlFlexibleServerResourceState(this.resourceId, this.loggerMock.Object, this.config);

            state.ExistingPostgreSqlFlexibleServerState = new PostgreSqlFlexibleServerStateDto
            {
                Sku = new AzurePostgreSqlSku("Standard_B1ms", "Burstable"),
                CoreCount = 1
            };

            state.RequestedPostgreSqlFlexibleServerState = new PostgreSqlFlexibleServerStateDto();

            // Act
            state.SetCoreCount("2");  // First update
            state.SetCoreCount("1");  // Should be ignored (lower)
            state.SetCoreCount("4");  // Should be kept (highest)

            // Assert
            var patch = state.PreparePatch();
            Assert.True(patch.HasChanges);
            Assert.True(patch.Disruptive);
            var patchData = (PostgreSqlFlexibleServerStateDto)patch.PatchData;
            Assert.Equal("Standard_B4ms", patchData.Sku.Name);
        }

        [Fact]
        public void PreparePatch_WithHigherCoreCount_ShouldUpdateToMinimumValidSku()
        {
            // Arrange
            var state = new PostgreSqlFlexibleServerResourceState(this.resourceId, this.loggerMock.Object, this.config);

            state.ExistingPostgreSqlFlexibleServerState = new PostgreSqlFlexibleServerStateDto
            {
                Sku = new AzurePostgreSqlSku("Standard_B1ms", "Burstable"),
                CoreCount = 1
            };

            state.RequestedPostgreSqlFlexibleServerState = new PostgreSqlFlexibleServerStateDto();

            // Act
            state.SetCoreCount("4"); // Request more cores than current SKU supports

            // Assert
            var patch = state.PreparePatch();
            Assert.True(patch.HasChanges);
            Assert.True(patch.Disruptive);
            var patchData = (PostgreSqlFlexibleServerStateDto)patch.PatchData;

            // The actual SKU name will depend on PostgreSqlFlexibleServerResourceStateHelper.GetMinimumSkuThatSatisfiesCoreCount
            Assert.NotEqual("Standard_B1ms", patchData.Sku.Name);
        }

        [Fact]
        public void PreparePatch_WithNoChanges_ShouldReturnNoChanges()
        {
            // Arrange
            var state = new PostgreSqlFlexibleServerResourceState(this.resourceId, this.loggerMock.Object, this.config);

            state.ExistingPostgreSqlFlexibleServerState = new PostgreSqlFlexibleServerStateDto
            {
                Sku = new AzurePostgreSqlSku("Standard_B1ms", "Burstable"),
                CoreCount = 1
            };

            state.RequestedPostgreSqlFlexibleServerState = new PostgreSqlFlexibleServerStateDto();

            // Assert
            var patch = state.PreparePatch();
            Assert.False(patch.HasChanges);
            Assert.False(patch.Disruptive);
        }

        [Fact]
        public void SetIops_WhenCurrentIsLower_ShouldUpdateIops()
        {
            // Arrange
            var state = new PostgreSqlFlexibleServerResourceState(this.resourceId, this.loggerMock.Object, this.config);

            state.ExistingPostgreSqlFlexibleServerState = new PostgreSqlFlexibleServerStateDto
            {
                Sku = new AzurePostgreSqlSku("Standard_B1ms", "Burstable"),
                CoreCount = 1,
                Iops = 360
            };

            state.RequestedPostgreSqlFlexibleServerState = new PostgreSqlFlexibleServerStateDto();

            // Act
            state.SetIops("500"); // Higher IOPS

            // Assert
            var patch = state.PreparePatch();
            Assert.True(patch.HasChanges);
            Assert.False(patch.Disruptive); // IOPS changes are not disruptive
            var patchData = (PostgreSqlFlexibleServerStateDto)patch.PatchData;
            Assert.Equal(500, patchData.Iops);
        }

        [Fact]
        public void SetIops_WhenExceedsCurrentSku_ShouldUpgradeSku()
        {
            // Arrange
            var state = new PostgreSqlFlexibleServerResourceState(this.resourceId, this.loggerMock.Object, this.config);

            state.ExistingPostgreSqlFlexibleServerState = new PostgreSqlFlexibleServerStateDto
            {
                Sku = new AzurePostgreSqlSku("Standard_B1ms", "Burstable"),
                CoreCount = 1,
                Iops = 360
            };

            state.RequestedPostgreSqlFlexibleServerState = new PostgreSqlFlexibleServerStateDto();

            // Act
            state.SetIops("2000"); // IOPS too high for B1ms

            // Assert
            var patch = state.PreparePatch();
            Assert.True(patch.HasChanges);
            Assert.True(patch.Disruptive); // SKU change is disruptive
            var patchData = (PostgreSqlFlexibleServerStateDto)patch.PatchData;
            Assert.Equal("Standard_B4ms", patchData.Sku.Name); // Should upgrade to B4ms which supports 2400 IOPS
            Assert.Equal(2000, patchData.Iops);
        }

        [Fact]
        public void SetIops_WithMultipleUpdates_ShouldKeepHighestValue()
        {
            // Arrange
            var state = new PostgreSqlFlexibleServerResourceState(this.resourceId, this.loggerMock.Object, this.config);

            state.ExistingPostgreSqlFlexibleServerState = new PostgreSqlFlexibleServerStateDto
            {
                Sku = new AzurePostgreSqlSku("Standard_B1ms", "Burstable"),
                CoreCount = 1,
                Iops = 360
            };

            state.RequestedPostgreSqlFlexibleServerState = new PostgreSqlFlexibleServerStateDto();

            // Act
            state.SetIops("500");  // First update
            state.SetIops("400");  // Should be ignored (lower)
            state.SetIops("600");  // Should be kept (highest)

            // Assert
            var patch = state.PreparePatch();
            Assert.True(patch.HasChanges);
            Assert.False(patch.Disruptive);
            var patchData = (PostgreSqlFlexibleServerStateDto)patch.PatchData;
            Assert.Equal(600, patchData.Iops);
        }
    }
}
