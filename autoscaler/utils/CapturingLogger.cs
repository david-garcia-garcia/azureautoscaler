using Microsoft.Extensions.Logging;

namespace poolautoscaler.utils
{
    /// <summary>
    /// A logger wrapper that captures log messages that would be lost (not shown) by the underlying logger
    /// and can replay them at a higher level when needed.
    ///
    /// This is useful when you want to capture debug-level messages during an operation
    /// and only promote them to INFO level if something significant happens (like a scale operation).
    ///
    /// The logger automatically determines what to capture based on the underlying logger's configured level.
    /// For example, if the underlying logger is set to INFO, this will capture DEBUG and TRACE messages.
    /// </summary>
    public class CapturingLogger : ILogger
    {
        private readonly ILogger innerLogger;
        private readonly List<CapturedLogEntry> buffer = new();

        /// <summary>
        /// Represents a captured log entry.
        /// </summary>
        public class CapturedLogEntry
        {
            /// <summary>Log level.</summary>
            public LogLevel Level { get; set; }

            /// <summary>Log message.</summary>
            public string Message { get; set; } = string.Empty;

            /// <summary>When the message was captured.</summary>
            public DateTime Timestamp { get; set; }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CapturingLogger"/> class that wraps the specified logger.
        /// Automatically captures all log messages that would be lost (below the underlying logger's level).
        /// </summary>
        /// <param name="innerLogger">The underlying logger to wrap.</param>
        public CapturingLogger(ILogger innerLogger)
        {
            this.innerLogger = innerLogger ?? throw new ArgumentNullException(nameof(innerLogger));
        }

        /// <summary>
        /// Gets the captured log entries.
        /// </summary>
        public IReadOnlyList<CapturedLogEntry> CapturedEntries => this.buffer.AsReadOnly();

        /// <summary>
        /// Clears the captured log entries without flushing them.
        /// </summary>
        public void Clear()
        {
            this.buffer.Clear();
        }

        /// <summary>
        /// Replays captured log entries at a new level, then clears the buffer.
        /// Use this to promote captured debug/trace messages to a visible level when something significant happens.
        /// </summary>
        /// <param name="targetLevel">The level at which to emit the captured messages.</param>
        /// <param name="originalLevels">Which original log levels to replay. If empty, replays all captured messages.</param>
        public void Replay(LogLevel targetLevel, params LogLevel[] originalLevels)
        {
            var levelsToReplay = originalLevels.Length > 0 ? new HashSet<LogLevel>(originalLevels) : null;

            foreach (var entry in this.buffer)
            {
                // If no filter specified, replay all; otherwise only replay matching levels
                if (levelsToReplay == null || levelsToReplay.Contains(entry.Level))
                {
                    this.innerLogger.Log(targetLevel, "[Captured {OriginalLevel}] {Message}", entry.Level, entry.Message);
                }
            }

            this.buffer.Clear();
        }

        /// <inheritdoc />
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            // Always pass through to the inner logger
            this.innerLogger.Log(logLevel, eventId, state, exception, formatter);

            // Capture messages that the inner logger won't show (below its configured level)
            // This way we can replay them at a higher level if needed
            if (!this.innerLogger.IsEnabled(logLevel))
            {
                var message = formatter(state, exception);
                this.buffer.Add(new CapturedLogEntry
                {
                    Level = logLevel,
                    Message = message,
                    Timestamp = DateTime.UtcNow
                });
            }
        }

        /// <inheritdoc />
        public bool IsEnabled(LogLevel logLevel)
        {
            return this.innerLogger.IsEnabled(logLevel);
        }

        /// <inheritdoc />
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return this.innerLogger.BeginScope(state);
        }
    }
}
