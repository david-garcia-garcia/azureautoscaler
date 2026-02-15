using Microsoft.Extensions.Logging;

namespace poolautoscaler.utils
{
    /// <summary>
    /// Extension methods for ILogger.
    /// </summary>
    public static class LoggerExtension
    {
        /// <summary>
        /// Gets the current minimum log level enabled for the logger.
        /// Iterates through all log levels and returns the first one that is enabled.
        /// </summary>
        /// <param name="logger">The logger instance.</param>
        /// <returns>The minimum enabled LogLevel, or LogLevel.None if no level is enabled.</returns>
        public static LogLevel CurrentLogLevel(this ILogger logger)
        {
            foreach (LogLevel logLevel in Enum.GetValues(typeof(LogLevel)))
            {
                if (logger.IsEnabled(logLevel))
                {
                    return logLevel;
                }
            }

            return LogLevel.None;
        }
    }
}
