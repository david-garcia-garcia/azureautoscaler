using Microsoft.Extensions.Logging;
using Moq;
using poolautoscaler.resources;

namespace poolautoscaler.tests
{
    public class ResourceStateTests
    {
        private readonly Mock<ILogger> _loggerMock;
        private readonly Resource _config;
        private readonly string _resourceId;

        public ResourceStateTests()
        {
            _loggerMock = new Mock<ILogger>();
            _resourceId = "/subscriptions/test/resourceGroups/test/providers/Microsoft.Sql/servers/test/databases/test";
            _config = new Resource { };
        }

        [Fact]
        public void IsDisabled_WhenNoDisabledEntries_ReturnsFalse()
        {
            // Arrange
            var state = new MsSqlDatabaseResourceState(_resourceId, _loggerMock.Object, _config);

            // Act
            var result = state.IsDisabled();

            // Assert
            Assert.False(result);
            Assert.Empty(state.DisabledUntil);
        }

        [Fact]
        public void IsDisabled_WhenPermanentlyDisabled_ReturnsTrue()
        {
            // Arrange
            var state = new MsSqlDatabaseResourceState(_resourceId, _loggerMock.Object, _config);
            state.DisabledUntil["permanent_reason"] = DateTime.MaxValue;

            // Act
            var result = state.IsDisabled();

            // Assert
            Assert.True(result);
            Assert.Single(state.DisabledUntil);
        }

        [Fact]
        public void IsDisabled_WhenTemporarilyDisabledInFuture_ReturnsTrue()
        {
            // Arrange
            var state = new MsSqlDatabaseResourceState(_resourceId, _loggerMock.Object, _config);
            state.DisabledUntil["cooldown"] = DateTime.UtcNow.AddMinutes(10);

            // Act
            var result = state.IsDisabled();

            // Assert
            Assert.True(result);
            Assert.Single(state.DisabledUntil);
        }

        [Fact]
        public void IsDisabled_WhenExpiredEntry_RemovesItAndReturnsFalse()
        {
            // Arrange
            var state = new MsSqlDatabaseResourceState(_resourceId, _loggerMock.Object, _config);
            state.DisabledUntil["expired_cooldown"] = DateTime.UtcNow.AddSeconds(-10); // Expired 10 seconds ago

            // Act
            var result = state.IsDisabled();

            // Assert
            Assert.False(result);
            Assert.Empty(state.DisabledUntil); // Expired entry should be removed
        }

        [Fact]
        public void IsDisabled_WhenMultipleExpiredEntries_RemovesAllExpired()
        {
            // Arrange
            var state = new MsSqlDatabaseResourceState(_resourceId, _loggerMock.Object, _config);
            state.DisabledUntil["expired1"] = DateTime.UtcNow.AddSeconds(-30);
            state.DisabledUntil["expired2"] = DateTime.UtcNow.AddSeconds(-20);
            state.DisabledUntil["expired3"] = DateTime.UtcNow.AddSeconds(-10);

            // Act
            var result = state.IsDisabled();

            // Assert
            Assert.False(result);
            Assert.Empty(state.DisabledUntil); // All expired entries should be removed
        }

        [Fact]
        public void IsDisabled_WhenMixedEntries_RemovesOnlyExpiredAndReturnsTrue()
        {
            // Arrange
            var state = new MsSqlDatabaseResourceState(_resourceId, _loggerMock.Object, _config);
            state.DisabledUntil["expired"] = DateTime.UtcNow.AddSeconds(-10); // Expired
            state.DisabledUntil["active"] = DateTime.UtcNow.AddMinutes(10);   // Still active

            // Act
            var result = state.IsDisabled();

            // Assert
            Assert.True(result); // Still disabled due to active entry
            Assert.Single(state.DisabledUntil); // Only active entry remains
            Assert.True(state.DisabledUntil.ContainsKey("active"));
            Assert.False(state.DisabledUntil.ContainsKey("expired"));
        }

        [Fact]
        public void IsDisabled_WhenPermanentAndExpiredMixed_RemovesExpiredButStaysDisabled()
        {
            // Arrange
            var state = new MsSqlDatabaseResourceState(_resourceId, _loggerMock.Object, _config);
            state.DisabledUntil["permanent"] = DateTime.MaxValue;           // Permanent
            state.DisabledUntil["expired"] = DateTime.UtcNow.AddSeconds(-10); // Expired

            // Act
            var result = state.IsDisabled();

            // Assert
            Assert.True(result); // Still disabled due to permanent entry
            Assert.Single(state.DisabledUntil); // Only permanent entry remains
            Assert.True(state.DisabledUntil.ContainsKey("permanent"));
            Assert.False(state.DisabledUntil.ContainsKey("expired"));
        }

        [Fact]
        public void IsDisabled_DoesNotRemovePermanentEntries()
        {
            // Arrange
            var state = new MsSqlDatabaseResourceState(_resourceId, _loggerMock.Object, _config);
            state.DisabledUntil["permanent"] = DateTime.MaxValue;

            // Act - Call multiple times
            state.IsDisabled();
            state.IsDisabled();
            var result = state.IsDisabled();

            // Assert
            Assert.True(result);
            Assert.Single(state.DisabledUntil);
            Assert.True(state.DisabledUntil.ContainsKey("permanent"));
            Assert.Equal(DateTime.MaxValue, state.DisabledUntil["permanent"]);
        }

        [Fact]
        public void IsDisabled_CalledMultipleTimes_CleansUpProgressively()
        {
            // Arrange
            var state = new MsSqlDatabaseResourceState(_resourceId, _loggerMock.Object, _config);
            state.DisabledUntil["key1"] = DateTime.UtcNow.AddSeconds(-5); // Already expired

            // First call - should clean up
            var result1 = state.IsDisabled();
            Assert.False(result1);
            Assert.Empty(state.DisabledUntil);

            // Add a new entry that will be active
            state.DisabledUntil["key2"] = DateTime.UtcNow.AddMilliseconds(100);

            // Second call - should return true (still active)
            var result2 = state.IsDisabled();
            Assert.True(result2);
            Assert.Single(state.DisabledUntil);

            // Wait for it to expire
            Thread.Sleep(150);

            // Third call - should clean up and return false
            var result3 = state.IsDisabled();
            Assert.False(result3);
            Assert.Empty(state.DisabledUntil);
        }
    }
}
