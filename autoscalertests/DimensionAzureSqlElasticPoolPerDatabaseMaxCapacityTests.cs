using Azure;
using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.Sql;
using Azure.ResourceManager.Sql.Models;
using Microsoft.Extensions.Logging;
using Moq;
using poolautoscaler.configuration;
using poolautoscaler.dimensions;
using poolautoscaler.resourcemanagement;
using poolautoscaler.resourcemanagement.Dto;
using poolautoscaler.resources.MssqlElasticPool;
using poolautoscaler.resources.MssqlElasticPool.Dto;

namespace poolautoscaler.tests
{
    public class DimensionAzureSqlElasticPoolPerDatabaseMaxCapacityTests
    {
        private readonly Mock<ILogger> loggerMock;
        private readonly DimensionAzureSqlElasticPoolPerDatabaseMaxCapacity dimension;
        private readonly string resourceId;
        private readonly Resource config;

        public DimensionAzureSqlElasticPoolPerDatabaseMaxCapacityTests()
        {
            this.loggerMock = new Mock<ILogger>();
            this.dimension = new DimensionAzureSqlElasticPoolPerDatabaseMaxCapacity();
            this.resourceId = "/subscriptions/test/resourceGroups/test/providers/Microsoft.Sql/servers/test/elasticPools/test";
            this.config = new Resource();
        }

        [Fact]
        public void CanApplyDimension_WithElasticPoolAndPerDatabaseMaxCapacity_ReturnsTrue()
        {
            var state = new MssqlElasticPoolResourceState(this.resourceId, this.loggerMock.Object, this.config);
            var mockPool = new Mock<ElasticPoolResource>();
            var poolData = new ElasticPoolData(AzureLocation.EastUS)
            {
                Sku = new SqlSku("StandardPool") { Capacity = 50 },
            };
            mockPool.SetupGet(p => p.Data).Returns(poolData);
            this.SetResourceOnState(state, mockPool.Object);
            var rule = new ScalingRule { Dimension = "PerDatabaseMaxCapacity" };

            Assert.True(this.dimension.CanApplyDimension(state, rule, this.loggerMock.Object));
        }

        [Fact]
        public void CanApplyDimension_WithWrongDimension_ReturnsFalse()
        {
            var state = new MssqlElasticPoolResourceState(this.resourceId, this.loggerMock.Object, this.config);
            var mockPool = new Mock<ElasticPoolResource>();
            mockPool.SetupGet(p => p.Data).Returns(new ElasticPoolData(AzureLocation.EastUS) { Sku = new SqlSku("StandardPool") { Capacity = 50 } });
            this.SetResourceOnState(state, mockPool.Object);
            var rule = new ScalingRule { Dimension = "Dtu" };

            Assert.False(this.dimension.CanApplyDimension(state, rule, this.loggerMock.Object));
        }

        [Fact]
        public void GetPerDbMaxCapacityValues_StandardPool100_ReturnsUpToPoolDtu()
        {
            var sku = new SqlSku("StandardPool");
            var values = MssqlElasticPoolResourceStateHelper.GetPerDbMaxCapacityValues(sku, 100);

            Assert.Equal(new[] { 10, 20, 50, 100 }, values);
        }

        [Fact]
        public void GetPerDbMaxCapacityValues_PremiumPool1500_CapsAt1000Not1500()
        {
            var sku = new SqlSku("PremiumPool");
            var values = MssqlElasticPoolResourceStateHelper.GetPerDbMaxCapacityValues(sku, 1500);

            Assert.Equal(new[] { 25, 50, 75, 125, 250, 500, 1000 }, values);
            Assert.DoesNotContain(1500, values);
        }

        [Fact]
        public async Task SetDimensionValue_OnStandardPool200_Snaps120To200()
        {
            var state = new MssqlElasticPoolResourceState(this.resourceId, this.loggerMock.Object, this.config);
            var mockPool = new Mock<ElasticPoolResource>();
            var sku = new SqlSku("StandardPool") { Capacity = 200 };
            var poolData = new ElasticPoolData(AzureLocation.EastUS)
            {
                Sku = sku,
                PerDatabaseSettings = new ElasticPoolPerDatabaseSettings { MaxCapacity = 100 },
            };
            mockPool.SetupGet(p => p.Data).Returns(poolData);
            this.SetResourceOnState(state, mockPool.Object);

            state.ExistingMssqlElasticPoolState = new MssqlElasticPoolState
            {
                Sku = new SqlSku("StandardPool") { Capacity = 200 },
                MaxSizeBytes = 107374182400,
                PerDatabaseMaxCapacity = 100,
            };
            state.RequestedMssqlElasticPoolState = new MssqlElasticPoolState();

            await this.dimension.SetDimensionValue(
                CancellationToken.None,
                state,
                this.loggerMock.Object,
                Mock.Of<TokenCredential>(),
                "120");

            Assert.Equal(200, state.RequestedMssqlElasticPoolState.PerDatabaseMaxCapacity);
        }

        [Fact]
        public async Task SetDimensionValue_OnPremiumPool1500_Clamps9999To1000()
        {
            var state = new MssqlElasticPoolResourceState(this.resourceId, this.loggerMock.Object, this.config);
            var mockPool = new Mock<ElasticPoolResource>();
            var sku = new SqlSku("PremiumPool") { Capacity = 1500 };
            var poolData = new ElasticPoolData(AzureLocation.EastUS)
            {
                Sku = sku,
                PerDatabaseSettings = new ElasticPoolPerDatabaseSettings { MaxCapacity = 1000 },
            };
            mockPool.SetupGet(p => p.Data).Returns(poolData);
            this.SetResourceOnState(state, mockPool.Object);

            state.ExistingMssqlElasticPoolState = new MssqlElasticPoolState
            {
                Sku = new SqlSku("PremiumPool") { Capacity = 1500 },
                MaxSizeBytes = 107374182400,
                PerDatabaseMaxCapacity = 1000,
            };
            state.RequestedMssqlElasticPoolState = new MssqlElasticPoolState();

            await this.dimension.SetDimensionValue(
                CancellationToken.None,
                state,
                this.loggerMock.Object,
                Mock.Of<TokenCredential>(),
                "9999");

            Assert.Equal(1000, state.RequestedMssqlElasticPoolState.PerDatabaseMaxCapacity);
        }

        [Fact]
        public async Task ApplyChanges_WithNullPerDatabaseMaxCapacity_SendsMaxCapacityEqualToSkuCapacity()
        {
            var state = new MssqlElasticPoolResourceState(this.resourceId, this.loggerMock.Object, this.config);
            var mockPool = new Mock<ElasticPoolResource>();
            ElasticPoolPatch? captured = null;
            var op = new Mock<ArmOperation<ElasticPoolResource>>();
            op.SetupGet(x => x.HasCompleted).Returns(true);
            mockPool
                .Setup(x => x.UpdateAsync(It.IsAny<WaitUntil>(), It.IsAny<ElasticPoolPatch>(), It.IsAny<CancellationToken>()))
                .Callback<WaitUntil, ElasticPoolPatch, CancellationToken>((_, p, _) => captured = p)
                .ReturnsAsync(op.Object);
            this.SetResourceOnState(state, mockPool.Object);

            var operation = new ResourcePatchOperation
            {
                PatchData = new MssqlElasticPoolState
                {
                    Sku = new SqlSku("StandardPool") { Capacity = 100 },
                    MaxSizeBytes = 107374182400L,
                    PerDatabaseMaxCapacity = null,
                },
            };

            await state.ApplyChanges(operation, CancellationToken.None);

            Assert.NotNull(captured?.PerDatabaseSettings);
            Assert.Equal(100.0, captured!.PerDatabaseSettings!.MaxCapacity);
        }

        [Fact]
        public async Task ApplyChanges_WithExplicitPerDatabaseMaxCapacity_SendsThatValue()
        {
            var state = new MssqlElasticPoolResourceState(this.resourceId, this.loggerMock.Object, this.config);
            var mockPool = new Mock<ElasticPoolResource>();
            ElasticPoolPatch? captured = null;
            var op = new Mock<ArmOperation<ElasticPoolResource>>();
            op.SetupGet(x => x.HasCompleted).Returns(true);
            mockPool
                .Setup(x => x.UpdateAsync(It.IsAny<WaitUntil>(), It.IsAny<ElasticPoolPatch>(), It.IsAny<CancellationToken>()))
                .Callback<WaitUntil, ElasticPoolPatch, CancellationToken>((_, p, _) => captured = p)
                .ReturnsAsync(op.Object);
            this.SetResourceOnState(state, mockPool.Object);

            var operation = new ResourcePatchOperation
            {
                PatchData = new MssqlElasticPoolState
                {
                    Sku = new SqlSku("StandardPool") { Capacity = 200 },
                    MaxSizeBytes = 107374182400L,
                    PerDatabaseMaxCapacity = 250,
                },
            };

            await state.ApplyChanges(operation, CancellationToken.None);

            Assert.NotNull(captured?.PerDatabaseSettings);
            Assert.Equal(250.0, captured!.PerDatabaseSettings!.MaxCapacity);
        }

        [Fact]
        public void PreparePatch_PerDatabaseMaxCapacityUnchanged_HasChangesFalse()
        {
            var state = new MssqlElasticPoolResourceState(this.resourceId, this.loggerMock.Object, this.config);
            var mockPool = new Mock<ElasticPoolResource>();
            mockPool.SetupGet(p => p.Data).Returns(new ElasticPoolData(AzureLocation.EastUS) { Sku = new SqlSku("StandardPool") { Capacity = 200 } });
            this.SetResourceOnState(state, mockPool.Object);

            state.ExistingMssqlElasticPoolState = new MssqlElasticPoolState
            {
                Sku = new SqlSku("StandardPool") { Capacity = 50 },
                MaxSizeBytes = 107374182400,
                PerDatabaseMaxCapacity = 100,
            };
            state.RequestedMssqlElasticPoolState = new MssqlElasticPoolState();
            state.SetPerDatabaseMaxCapacity(100);

            var patch = state.PreparePatch();
            Assert.False(patch.HasChanges);
        }

        [Fact]
        public void PreparePatch_PerDatabaseMaxCapacityChanged_HasChangesTrue()
        {
            var state = new MssqlElasticPoolResourceState(this.resourceId, this.loggerMock.Object, this.config);
            var mockPool = new Mock<ElasticPoolResource>();
            mockPool.SetupGet(p => p.Data).Returns(new ElasticPoolData(AzureLocation.EastUS) { Sku = new SqlSku("StandardPool") { Capacity = 200 } });
            this.SetResourceOnState(state, mockPool.Object);

            state.ExistingMssqlElasticPoolState = new MssqlElasticPoolState
            {
                Sku = new SqlSku("StandardPool") { Capacity = 50 },
                MaxSizeBytes = 107374182400,
                PerDatabaseMaxCapacity = 100,
            };
            state.RequestedMssqlElasticPoolState = new MssqlElasticPoolState();
            state.SetPerDatabaseMaxCapacity(200);

            var patch = state.PreparePatch();
            Assert.True(patch.HasChanges);
        }

        private void SetResourceOnState(MssqlElasticPoolResourceState state, ElasticPoolResource pool)
        {
            var resourceField = typeof(ResourceState).GetField(
                "Resource",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            Assert.NotNull(resourceField);
            resourceField!.SetValue(state, pool);
        }
    }
}
