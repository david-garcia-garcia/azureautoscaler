using Microsoft.Extensions.Logging;
using Moq;
using poolautoscaler.utils;

namespace poolautoscaler.tests
{
    public class CapturingLoggerTests
    {
        private readonly Mock<ILogger> _innerLoggerMock;

        public CapturingLoggerTests()
        {
            _innerLoggerMock = new Mock<ILogger>();
        }

        /// <summary>
        /// Helper to setup the mock logger with a specific minimum log level.
        /// Messages at or above this level will be "shown" (IsEnabled returns true).
        /// Messages below this level will be "lost" (IsEnabled returns false) and should be captured.
        /// </summary>
        private void SetupLoggerLevel(LogLevel minLevel)
        {
            foreach (LogLevel level in Enum.GetValues(typeof(LogLevel)))
            {
                _innerLoggerMock.Setup(l => l.IsEnabled(level)).Returns(level >= minLevel);
            }
        }

        [Fact]
        public void Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new CapturingLogger(null!));
        }

        [Fact]
        public void Log_AlwaysPassesThroughToInnerLogger()
        {
            // Arrange
            SetupLoggerLevel(LogLevel.Information);
            var capturingLogger = new CapturingLogger(_innerLoggerMock.Object);

            // Act
            capturingLogger.LogInformation("Test message");

            // Assert - verify inner logger was called
            _innerLoggerMock.Verify(
                l => l.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception?>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public void Log_CapturesMessagesNotEnabledByInnerLogger()
        {
            // Arrange - inner logger is set to INFO, so DEBUG won't be shown
            SetupLoggerLevel(LogLevel.Information);
            var capturingLogger = new CapturingLogger(_innerLoggerMock.Object);

            // Act
            capturingLogger.LogDebug("Test debug message");

            // Assert - DEBUG is below INFO, so it should be captured
            Assert.Single(capturingLogger.CapturedEntries);
            Assert.Equal(LogLevel.Debug, capturingLogger.CapturedEntries[0].Level);
            Assert.Equal("Test debug message", capturingLogger.CapturedEntries[0].Message);
        }

        [Fact]
        public void Log_DoesNotCaptureMessagesEnabledByInnerLogger()
        {
            // Arrange - inner logger is set to INFO
            SetupLoggerLevel(LogLevel.Information);
            var capturingLogger = new CapturingLogger(_innerLoggerMock.Object);

            // Act
            capturingLogger.LogInformation("Test info message");
            capturingLogger.LogWarning("Test warning message");

            // Assert - INFO and above are shown by inner logger, so not captured
            Assert.Empty(capturingLogger.CapturedEntries);
        }

        [Fact]
        public void Log_WhenInnerLoggerAtWarning_CapturesInfoAndBelow()
        {
            // Arrange - inner logger only shows WARNING and above
            SetupLoggerLevel(LogLevel.Warning);
            var capturingLogger = new CapturingLogger(_innerLoggerMock.Object);

            // Act
            capturingLogger.LogTrace("Trace");
            capturingLogger.LogDebug("Debug");
            capturingLogger.LogInformation("Info");
            capturingLogger.LogWarning("Warning");
            capturingLogger.LogError("Error");

            // Assert - Trace, Debug, Info should be captured; Warning, Error should not
            Assert.Equal(3, capturingLogger.CapturedEntries.Count);
            Assert.Contains(capturingLogger.CapturedEntries, e => e.Level == LogLevel.Trace);
            Assert.Contains(capturingLogger.CapturedEntries, e => e.Level == LogLevel.Debug);
            Assert.Contains(capturingLogger.CapturedEntries, e => e.Level == LogLevel.Information);
            Assert.DoesNotContain(capturingLogger.CapturedEntries, e => e.Level == LogLevel.Warning);
            Assert.DoesNotContain(capturingLogger.CapturedEntries, e => e.Level == LogLevel.Error);
        }

        [Fact]
        public void Log_WhenInnerLoggerAtTrace_CapturesNothing()
        {
            // Arrange - inner logger shows everything
            SetupLoggerLevel(LogLevel.Trace);
            var capturingLogger = new CapturingLogger(_innerLoggerMock.Object);

            // Act
            capturingLogger.LogTrace("Trace");
            capturingLogger.LogDebug("Debug");
            capturingLogger.LogInformation("Info");

            // Assert - nothing should be captured since inner logger shows everything
            Assert.Empty(capturingLogger.CapturedEntries);
        }

        [Fact]
        public void Clear_RemovesCapturedEntries()
        {
            // Arrange
            SetupLoggerLevel(LogLevel.Information);
            var capturingLogger = new CapturingLogger(_innerLoggerMock.Object);
            capturingLogger.LogDebug("Message 1");
            capturingLogger.LogDebug("Message 2");
            Assert.Equal(2, capturingLogger.CapturedEntries.Count);

            // Act
            capturingLogger.Clear();

            // Assert
            Assert.Empty(capturingLogger.CapturedEntries);
        }

        [Fact]
        public void Replay_EmitsCapturedMessagesAtTargetLevel()
        {
            // Arrange
            SetupLoggerLevel(LogLevel.Information);
            var capturingLogger = new CapturingLogger(_innerLoggerMock.Object);
            capturingLogger.LogDebug("Debug message 1");
            capturingLogger.LogDebug("Debug message 2");

            // Act
            capturingLogger.Replay(LogLevel.Information);

            // Assert - should have called Log with Information level twice (for the replay)
            _innerLoggerMock.Verify(
                l => l.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception?>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Exactly(2));
        }

        [Fact]
        public void Replay_ClearsBufferAfterReplaying()
        {
            // Arrange
            SetupLoggerLevel(LogLevel.Information);
            var capturingLogger = new CapturingLogger(_innerLoggerMock.Object);
            capturingLogger.LogDebug("Debug message");
            Assert.Single(capturingLogger.CapturedEntries);

            // Act
            capturingLogger.Replay(LogLevel.Information);

            // Assert
            Assert.Empty(capturingLogger.CapturedEntries);
        }

        [Fact]
        public void Replay_EmitsAtSpecifiedLevel()
        {
            // Arrange
            SetupLoggerLevel(LogLevel.Information);
            var capturingLogger = new CapturingLogger(_innerLoggerMock.Object);
            capturingLogger.LogDebug("Debug message");

            // Act
            capturingLogger.Replay(LogLevel.Warning);

            // Assert
            _innerLoggerMock.Verify(
                l => l.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception?>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public void Replay_WithLevelFilter_OnlyReplaysMatchingLevels()
        {
            // Arrange - inner logger is set to WARNING, so DEBUG and TRACE are captured
            // (INFO is also captured but we won't log any INFO in this test)
            SetupLoggerLevel(LogLevel.Warning);
            var capturingLogger = new CapturingLogger(_innerLoggerMock.Object);
            capturingLogger.LogTrace("Trace message");
            capturingLogger.LogDebug("Debug message");
            Assert.Equal(2, capturingLogger.CapturedEntries.Count);

            // Reset mock to only count replay calls
            _innerLoggerMock.Invocations.Clear();

            // Act - only replay Debug messages (not Trace)
            capturingLogger.Replay(LogLevel.Warning, LogLevel.Debug);

            // Assert - only 1 message should be replayed (the Debug one)
            _innerLoggerMock.Verify(
                l => l.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception?>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public void Replay_WithMultipleLevelFilters_ReplaysAllMatchingLevels()
        {
            // Arrange - inner logger is set to ERROR, so TRACE, DEBUG and INFO are captured
            SetupLoggerLevel(LogLevel.Error);
            var capturingLogger = new CapturingLogger(_innerLoggerMock.Object);
            capturingLogger.LogTrace("Trace message");
            capturingLogger.LogDebug("Debug message 1");
            capturingLogger.LogDebug("Debug message 2");
            capturingLogger.LogInformation("Info message");
            Assert.Equal(4, capturingLogger.CapturedEntries.Count);

            // Reset mock to only count replay calls
            _innerLoggerMock.Invocations.Clear();

            // Act - replay Debug and Info messages (not Trace)
            capturingLogger.Replay(LogLevel.Warning, LogLevel.Debug, LogLevel.Information);

            // Assert - 3 messages should be replayed (2 Debug + 1 Info)
            _innerLoggerMock.Verify(
                l => l.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception?>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Exactly(3));
        }

        [Fact]
        public void Replay_WithNoMatchingLevels_ReplaysNothing()
        {
            // Arrange
            SetupLoggerLevel(LogLevel.Warning);
            var capturingLogger = new CapturingLogger(_innerLoggerMock.Object);
            capturingLogger.LogDebug("Debug message");

            // Reset mock to only count replay calls
            _innerLoggerMock.Invocations.Clear();

            // Act - filter for Trace only, but we only have Debug
            capturingLogger.Replay(LogLevel.Warning, LogLevel.Trace);

            // Assert - nothing should be replayed
            _innerLoggerMock.Verify(
                l => l.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception?>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Never);
        }

        [Fact]
        public void Replay_WithLevelFilter_StillClearsBuffer()
        {
            // Arrange
            SetupLoggerLevel(LogLevel.Warning);
            var capturingLogger = new CapturingLogger(_innerLoggerMock.Object);
            capturingLogger.LogTrace("Trace message");
            capturingLogger.LogDebug("Debug message");

            // Act - only replay Debug (not Trace)
            capturingLogger.Replay(LogLevel.Warning, LogLevel.Debug);

            // Assert - buffer should be cleared even though not all messages were replayed
            Assert.Empty(capturingLogger.CapturedEntries);
        }

        [Fact]
        public void Replay_WithEmptyBuffer_DoesNothing()
        {
            // Arrange
            SetupLoggerLevel(LogLevel.Information);
            var capturingLogger = new CapturingLogger(_innerLoggerMock.Object);

            // Act
            capturingLogger.Replay(LogLevel.Information);

            // Assert - no Info level logs should be emitted for replay
            _innerLoggerMock.Verify(
                l => l.Log(
                    LogLevel.Information,
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception?>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Never);
        }

        [Fact]
        public void IsEnabled_DelegatesToInnerLogger()
        {
            // Arrange
            _innerLoggerMock.Setup(l => l.IsEnabled(LogLevel.Debug)).Returns(false);
            _innerLoggerMock.Setup(l => l.IsEnabled(LogLevel.Information)).Returns(true);
            var capturingLogger = new CapturingLogger(_innerLoggerMock.Object);

            // Act & Assert
            Assert.False(capturingLogger.IsEnabled(LogLevel.Debug));
            Assert.True(capturingLogger.IsEnabled(LogLevel.Information));
        }

        [Fact]
        public void BeginScope_DelegatesToInnerLogger()
        {
            // Arrange
            var scopeMock = new Mock<IDisposable>();
            _innerLoggerMock.Setup(l => l.BeginScope(It.IsAny<object>())).Returns(scopeMock.Object);
            var capturingLogger = new CapturingLogger(_innerLoggerMock.Object);

            // Act
            var scope = capturingLogger.BeginScope("test scope");

            // Assert
            Assert.Same(scopeMock.Object, scope);
            _innerLoggerMock.Verify(l => l.BeginScope("test scope"), Times.Once);
        }

        [Fact]
        public void CapturedEntries_ContainsTimestamp()
        {
            // Arrange
            SetupLoggerLevel(LogLevel.Information);
            var capturingLogger = new CapturingLogger(_innerLoggerMock.Object);
            var beforeCapture = DateTime.UtcNow;

            // Act
            capturingLogger.LogDebug("Test message");
            var afterCapture = DateTime.UtcNow;

            // Assert
            var entry = capturingLogger.CapturedEntries[0];
            Assert.True(entry.Timestamp >= beforeCapture);
            Assert.True(entry.Timestamp <= afterCapture);
        }

        [Fact]
        public void Clear_AllowsNewCaptureCycle()
        {
            // Arrange
            SetupLoggerLevel(LogLevel.Information);
            var capturingLogger = new CapturingLogger(_innerLoggerMock.Object);

            // First cycle
            capturingLogger.LogDebug("Cycle 1 message");
            Assert.Single(capturingLogger.CapturedEntries);
            capturingLogger.Clear();

            // Second cycle
            capturingLogger.LogDebug("Cycle 2 message 1");
            capturingLogger.LogDebug("Cycle 2 message 2");

            // Assert
            Assert.Equal(2, capturingLogger.CapturedEntries.Count);
            Assert.Equal("Cycle 2 message 1", capturingLogger.CapturedEntries[0].Message);
        }

        [Fact]
        public void Log_CapturesFormattedMessageWithArguments()
        {
            // Arrange
            SetupLoggerLevel(LogLevel.Information);
            var capturingLogger = new CapturingLogger(_innerLoggerMock.Object);

            // Act
            capturingLogger.LogDebug("Value is {0} and name is {1}", 42, "test");

            // Assert
            Assert.Single(capturingLogger.CapturedEntries);
            Assert.Contains("42", capturingLogger.CapturedEntries[0].Message);
            Assert.Contains("test", capturingLogger.CapturedEntries[0].Message);
        }

        [Fact]
        public void Log_CapturesBehaviorAdaptsToLoggerLevel()
        {
            // This test verifies that capture behavior is based on IsEnabled at log time,
            // not a fixed capture level

            // Arrange - start with INFO level
            SetupLoggerLevel(LogLevel.Information);
            var capturingLogger = new CapturingLogger(_innerLoggerMock.Object);

            // Act - log a debug message (should be captured since INFO doesn't show DEBUG)
            capturingLogger.LogDebug("Debug when INFO");

            // Assert
            Assert.Single(capturingLogger.CapturedEntries);

            // Now change to DEBUG level
            SetupLoggerLevel(LogLevel.Debug);

            // Act - log another debug message (should NOT be captured since DEBUG shows DEBUG)
            capturingLogger.LogDebug("Debug when DEBUG");

            // Assert - still only one captured (the first one)
            Assert.Single(capturingLogger.CapturedEntries);
            Assert.Equal("Debug when INFO", capturingLogger.CapturedEntries[0].Message);
        }
    }
}
