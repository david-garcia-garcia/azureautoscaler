using Azure.Core;
using Azure.ResourceManager.Sql;
using Azure.ResourceManager.Sql.Models;
using Microsoft.Extensions.Logging;
using Moq;
using poolautoscaler.configuration;
using poolautoscaler.dimensions;
using poolautoscaler.resourcemanagement;
using poolautoscaler.resources.MssqlElasticPool;
using poolautoscaler.resources.MssqlElasticPool.Dto;
using poolautoscaler.utils;

namespace poolautoscaler.tests
{
    /// <summary>
    /// DTU dimension must accept strings produced by forecast / floating-point metrics (e.g. 28.000000000000004)
    /// for Compare, validation, and apply paths that otherwise use int.TryParse / int.Parse.
    /// </summary>
    public class DimensionAzureSqlElasticPoolDtuTests
    {
        private readonly Mock<ILogger> loggerMock;
        private readonly DimensionAzureSqlElasticPoolDtu dimension;
        private readonly string resourceId;
        private readonly Resource config;

        public DimensionAzureSqlElasticPoolDtuTests()
        {
            this.loggerMock = new Mock<ILogger>();
            this.dimension = new DimensionAzureSqlElasticPoolDtu();
            this.resourceId = "/subscriptions/test/resourceGroups/test/providers/Microsoft.Sql/servers/test/elasticPools/test";
            this.config = new Resource();
        }

        [Fact]
        public void Compare_WithForecastStyleFloatString_AndIntegerMax_DoesNotThrow_AndComparesAsRoundedIntegers()
        {
            var mockPool = new Mock<ElasticPoolResource>();

            // Same scenario as RuleStrategyFixed min/max: Value1 from metric, Value2 from config
            var cmp = this.dimension.Compare(mockPool.Object, "28.000000000000004", "200");

            Assert.True(cmp < 0);
        }

        [Fact]
        public void Compare_WithFloatString_OnEitherSide_IsSymmetric()
        {
            var mockPool = new Mock<ElasticPoolResource>();

            // 28.000000000000004 ceilings to 29; 200 > 29
            Assert.True(this.dimension.Compare(mockPool.Object, "200", "28.000000000000004") > 0);

            // 50.1 ceilings to 51; 100 > 51
            Assert.True(this.dimension.Compare(mockPool.Object, "100", "50.1") > 0);
        }

        [Fact]
        public void Compare_WithInvalidNonNumericString_StillThrows()
        {
            var mockPool = new Mock<ElasticPoolResource>();

            var ex = Assert.Throws<ArgumentException>(() =>
                this.dimension.Compare(mockPool.Object, "not-a-number", "200"));
            Assert.Contains("Invalid dimension values", ex.Message);
        }

        [Fact]
        public void Compare_WithFractionalDtu_CeilsToNearestInteger()
        {
            var mockPool = new Mock<ElasticPoolResource>();

            // 100.5 ceils to 101, which is still less than 200
            var cmp = this.dimension.Compare(mockPool.Object, "100.5", "200");
            Assert.True(cmp < 0);
        }

        [Fact]
        public async Task SetDimensionValue_WithNearIntegerFloatString_ParsesToRoundedInt_AndUpdatesRequestedState()
        {
            var state = new MssqlElasticPoolResourceState(this.resourceId, this.loggerMock.Object, this.config);
            var mockPool = new Mock<ElasticPoolResource>();
            this.SetResourceOnState(state, mockPool.Object);

            state.ExistingMssqlElasticPoolState = new MssqlElasticPoolState
            {
                Sku = new SqlSku("StandardPool") { Capacity = 50 },
                MaxSizeBytes = 107374182400,
            };
            state.RequestedMssqlElasticPoolState = new MssqlElasticPoolState();

            // 49.3 ceilings to 50, which is a valid Standard DTU tier
            await this.dimension.SetDimensionValue(
                CancellationToken.None,
                state,
                this.loggerMock.Object,
                Mock.Of<TokenCredential>(),
                "49.3");

            Assert.Equal(50, state.RequestedMssqlElasticPoolState.Sku!.Capacity);
        }

        [Fact]
        public void GetNextDimensionValue_WithNearIntegerFloatString_ReturnsNextStandardTier()
        {
            var state = new MssqlElasticPoolResourceState(this.resourceId, this.loggerMock.Object, this.config);
            var mockPool = new Mock<ElasticPoolResource>();
            var sku = new SqlSku("StandardPool") { Capacity = 100 };
            var poolData = new ElasticPoolData(AzureLocation.EastUS) { Sku = sku };
            mockPool.SetupGet(p => p.Data).Returns(poolData);
            this.SetResourceOnState(state, mockPool.Object);

            // 99.1 ceilings to 100, which is in the Standard tier list; next tier is 200
            var next = this.dimension.GetNextDimensionValue(state, "99.1");

            Assert.Equal("200", next);
        }

        [Fact]
        public void InternalGetPreviousDimensionValue_WithNearIntegerFloatString_ReturnsPreviousTier()
        {
            var state = new MssqlElasticPoolResourceState(this.resourceId, this.loggerMock.Object, this.config);
            var mockPool = new Mock<ElasticPoolResource>();
            var sku = new SqlSku("StandardPool") { Capacity = 100 };
            var poolData = new ElasticPoolData(AzureLocation.EastUS) { Sku = sku };
            mockPool.SetupGet(p => p.Data).Returns(poolData);
            this.SetResourceOnState(state, mockPool.Object);

            // 99.1 ceilings to 100, which is in the Standard tier list; previous tier is 50
            var prev = this.dimension.InternalGetPreviousDimensionValue(state, "99.1");

            Assert.Equal("50", prev);
        }

        [Theory]
        [InlineData("28", 28)]
        [InlineData("200", 200)]
        [InlineData("28.000000000000004", 29)]  // floating-point artifact: ceilings to 29
        [InlineData("100.00000000000001", 101)] // floating-point artifact: ceilings to 101
        [InlineData("100.5", 101)]              // genuine fraction: ceils up
        [InlineData("100.1", 101)]              // genuine fraction: ceils up
        [InlineData("100.9", 101)]              // genuine fraction: ceils up
        public void IntParseUtils_TryParseInt_CeilsDecimalsToInteger(string input, int expected)
        {
            Assert.True(IntParseUtils.TryParseInt(input, out var result));
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("not-a-number")]
        [InlineData("abc")]
        [InlineData("")]
        public void IntParseUtils_TryParseInt_ReturnsFalseForNonNumeric(string input)
        {
            Assert.False(IntParseUtils.TryParseInt(input, out _));
        }

        [Fact]
        public void IntParseUtils_ParseInt_ThrowsForNonNumericString()
        {
            var ex = Assert.Throws<ArgumentException>(() => IntParseUtils.ParseInt("not-a-number"));
            Assert.Contains("Cannot parse", ex.Message);
        }

        private void SetResourceOnState(MssqlElasticPoolResourceState state, ElasticPoolResource pool)
        {
            var resourceField = typeof(ResourceState).GetField(
                "Resource",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            Assert.NotNull(resourceField);
            resourceField.SetValue(state, pool);
        }
    }
}
