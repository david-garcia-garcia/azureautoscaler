using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace poolautoscaler.utils
{
    /// <summary>
    /// Extension methods for ILogger
    /// </summary>
    public static class LoggerExtension
    {
        /// <summary>
        /// Gets the current minimum log level enabled for the logger.
        /// Iterates through all log levels and returns the first one that is enabled.
        /// </summary>
        /// <param name="logger">The logger instance</param>
        /// <returns>The minimum enabled LogLevel, or LogLevel.None if no level is enabled</returns>
        public static LogLevel CurrentLogLevel(this ILogger logger)
        {
            foreach (LogLevel logLevel in Enum.GetValues(typeof(LogLevel)))
            {
                if (logger.IsEnabled(logLevel))
                    return logLevel;
            }

            return LogLevel.None;
        }
    }

    public static class HelperExtensions
    {
        /// <summary>
        /// Convert a single element to a list.
        /// </summary>
        /// <typeparam name="TObjectType"></typeparam>
        /// <param name="source"></param>
        /// <returns></returns>
        public static IEnumerable<TObjectType> AsIterable<TObjectType>(this IEnumerable<TObjectType> source)
        {
            if (source != null)
            {
                return source;
            }

            return new List<TObjectType>();
        }

        public static string SerializeSimple(object data)
        {
            return JsonSerializer.Serialize(data, new JsonSerializerOptions() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });
        }

        public static T DeepCopy<T>(this T self)
        {
            var serialized = JsonSerializer.Serialize(self);
            return JsonSerializer.Deserialize<T>(serialized);
        }

        /// <summary>
        /// Attempts to remove the value with the specified key from the dictionary.
        /// Returns true if the key was found and removed; otherwise, false.
        /// </summary>
        /// <typeparam name="TKey">The type of keys in the dictionary</typeparam>
        /// <typeparam name="TValue">The type of values in the dictionary</typeparam>
        /// <param name="dictionary">The dictionary to remove from</param>
        /// <param name="key">The key to remove</param>
        /// <returns>True if the key was found and removed; otherwise, false</returns>
        public static bool TryRemove<TKey, TValue>(this IDictionary<TKey, TValue> dictionary, TKey key)
        {
            if (dictionary == null)
            {
                return false;
            }

            return dictionary.Remove(key);
        }
    }
}
