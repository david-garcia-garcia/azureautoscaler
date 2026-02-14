using Microsoft.Extensions.Logging;
using Moq;
using poolautoscaler.utils;

namespace poolautoscaler.tests
{
    public class LoggerExtensionTests
    {
        [Fact]
        public void CurrentLogLevel_WhenTraceEnabled_ReturnsTrace()
        {
            // Arrange
            var loggerMock = new Mock<ILogger>();
            loggerMock.Setup(l => l.IsEnabled(LogLevel.Trace)).Returns(true);

            // Act
            var result = loggerMock.Object.CurrentLogLevel();

            // Assert
            Assert.Equal(LogLevel.Trace, result);
        }

        [Fact]
        public void CurrentLogLevel_WhenDebugIsMinimum_ReturnsDebug()
        {
            // Arrange
            var loggerMock = new Mock<ILogger>();
            loggerMock.Setup(l => l.IsEnabled(LogLevel.Trace)).Returns(false);
            loggerMock.Setup(l => l.IsEnabled(LogLevel.Debug)).Returns(true);

            // Act
            var result = loggerMock.Object.CurrentLogLevel();

            // Assert
            Assert.Equal(LogLevel.Debug, result);
        }

        [Fact]
        public void CurrentLogLevel_WhenInformationIsMinimum_ReturnsInformation()
        {
            // Arrange
            var loggerMock = new Mock<ILogger>();
            loggerMock.Setup(l => l.IsEnabled(LogLevel.Trace)).Returns(false);
            loggerMock.Setup(l => l.IsEnabled(LogLevel.Debug)).Returns(false);
            loggerMock.Setup(l => l.IsEnabled(LogLevel.Information)).Returns(true);

            // Act
            var result = loggerMock.Object.CurrentLogLevel();

            // Assert
            Assert.Equal(LogLevel.Information, result);
        }

        [Fact]
        public void CurrentLogLevel_WhenWarningIsMinimum_ReturnsWarning()
        {
            // Arrange
            var loggerMock = new Mock<ILogger>();
            loggerMock.Setup(l => l.IsEnabled(LogLevel.Trace)).Returns(false);
            loggerMock.Setup(l => l.IsEnabled(LogLevel.Debug)).Returns(false);
            loggerMock.Setup(l => l.IsEnabled(LogLevel.Information)).Returns(false);
            loggerMock.Setup(l => l.IsEnabled(LogLevel.Warning)).Returns(true);

            // Act
            var result = loggerMock.Object.CurrentLogLevel();

            // Assert
            Assert.Equal(LogLevel.Warning, result);
        }

        [Fact]
        public void CurrentLogLevel_WhenErrorIsMinimum_ReturnsError()
        {
            // Arrange
            var loggerMock = new Mock<ILogger>();
            loggerMock.Setup(l => l.IsEnabled(LogLevel.Trace)).Returns(false);
            loggerMock.Setup(l => l.IsEnabled(LogLevel.Debug)).Returns(false);
            loggerMock.Setup(l => l.IsEnabled(LogLevel.Information)).Returns(false);
            loggerMock.Setup(l => l.IsEnabled(LogLevel.Warning)).Returns(false);
            loggerMock.Setup(l => l.IsEnabled(LogLevel.Error)).Returns(true);

            // Act
            var result = loggerMock.Object.CurrentLogLevel();

            // Assert
            Assert.Equal(LogLevel.Error, result);
        }

        [Fact]
        public void CurrentLogLevel_WhenCriticalIsMinimum_ReturnsCritical()
        {
            // Arrange
            var loggerMock = new Mock<ILogger>();
            loggerMock.Setup(l => l.IsEnabled(LogLevel.Trace)).Returns(false);
            loggerMock.Setup(l => l.IsEnabled(LogLevel.Debug)).Returns(false);
            loggerMock.Setup(l => l.IsEnabled(LogLevel.Information)).Returns(false);
            loggerMock.Setup(l => l.IsEnabled(LogLevel.Warning)).Returns(false);
            loggerMock.Setup(l => l.IsEnabled(LogLevel.Error)).Returns(false);
            loggerMock.Setup(l => l.IsEnabled(LogLevel.Critical)).Returns(true);

            // Act
            var result = loggerMock.Object.CurrentLogLevel();

            // Assert
            Assert.Equal(LogLevel.Critical, result);
        }

        [Fact]
        public void CurrentLogLevel_WhenNoLevelEnabled_ReturnsNone()
        {
            // Arrange
            var loggerMock = new Mock<ILogger>();
            loggerMock.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(false);

            // Act
            var result = loggerMock.Object.CurrentLogLevel();

            // Assert
            Assert.Equal(LogLevel.None, result);
        }

        [Fact]
        public void CurrentLogLevel_ReturnsLowestEnabledLevel()
        {
            // Arrange - All levels from Information and above are enabled
            var loggerMock = new Mock<ILogger>();
            loggerMock.Setup(l => l.IsEnabled(LogLevel.Trace)).Returns(false);
            loggerMock.Setup(l => l.IsEnabled(LogLevel.Debug)).Returns(false);
            loggerMock.Setup(l => l.IsEnabled(LogLevel.Information)).Returns(true);
            loggerMock.Setup(l => l.IsEnabled(LogLevel.Warning)).Returns(true);
            loggerMock.Setup(l => l.IsEnabled(LogLevel.Error)).Returns(true);
            loggerMock.Setup(l => l.IsEnabled(LogLevel.Critical)).Returns(true);

            // Act
            var result = loggerMock.Object.CurrentLogLevel();

            // Assert - Should return Information as it's the lowest enabled
            Assert.Equal(LogLevel.Information, result);
        }

        [Theory]
        [InlineData(LogLevel.Trace)]
        [InlineData(LogLevel.Debug)]
        [InlineData(LogLevel.Information)]
        [InlineData(LogLevel.Warning)]
        [InlineData(LogLevel.Error)]
        [InlineData(LogLevel.Critical)]
        public void CurrentLogLevel_WithSingleLevelEnabled_ReturnsThatLevel(LogLevel expectedLevel)
        {
            // Arrange
            var loggerMock = new Mock<ILogger>();

            // Enable only the expected level
            foreach (LogLevel level in Enum.GetValues(typeof(LogLevel)))
            {
                loggerMock.Setup(l => l.IsEnabled(level)).Returns(level == expectedLevel);
            }

            // Act
            var result = loggerMock.Object.CurrentLogLevel();

            // Assert
            Assert.Equal(expectedLevel, result);
        }

        [Fact]
        public void CurrentLogLevel_GreaterThanDebug_CanBeUsedForConditionalLogging()
        {
            // Arrange - Log level is Information (greater than Debug)
            var loggerMock = new Mock<ILogger>();
            loggerMock.Setup(l => l.IsEnabled(LogLevel.Trace)).Returns(false);
            loggerMock.Setup(l => l.IsEnabled(LogLevel.Debug)).Returns(false);
            loggerMock.Setup(l => l.IsEnabled(LogLevel.Information)).Returns(true);

            // Act
            var currentLevel = loggerMock.Object.CurrentLogLevel();
            var isGreaterThanDebug = currentLevel > LogLevel.Debug;

            // Assert
            Assert.True(isGreaterThanDebug);
            Assert.Equal(LogLevel.Information, currentLevel);
        }

        [Fact]
        public void CurrentLogLevel_AtDebug_NotGreaterThanDebug()
        {
            // Arrange - Log level is Debug
            var loggerMock = new Mock<ILogger>();
            loggerMock.Setup(l => l.IsEnabled(LogLevel.Trace)).Returns(false);
            loggerMock.Setup(l => l.IsEnabled(LogLevel.Debug)).Returns(true);

            // Act
            var currentLevel = loggerMock.Object.CurrentLogLevel();
            var isGreaterThanDebug = currentLevel > LogLevel.Debug;

            // Assert
            Assert.False(isGreaterThanDebug);
            Assert.Equal(LogLevel.Debug, currentLevel);
        }
    }
}
