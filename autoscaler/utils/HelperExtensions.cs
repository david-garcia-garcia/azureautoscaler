using System.Text.Json;
using System.Text.Json.Serialization;

namespace poolautoscaler.utils
{
    /// <summary>Extension methods and serialization helpers.</summary>
    public static class HelperExtensions
    {
        /// <summary>
        /// Convert a single element to a list.
        /// </summary>
        /// <typeparam name="TObjectType">Element type.</typeparam>
        /// <param name="source">Source enumerable.</param>
        /// <returns>The source sequence or an empty list if null.</returns>
        public static IEnumerable<TObjectType> AsIterable<TObjectType>(this IEnumerable<TObjectType> source)
        {
            if (source != null)
            {
                return source;
            }

            return new List<TObjectType>();
        }

        /// <summary>Serializes to JSON (nulls omitted).</summary>
        /// <param name="data">The object to serialize.</param>
        /// <returns>JSON string.</returns>
        public static string SerializeSimple(object data)
        {
            return JsonSerializer.Serialize(data, new JsonSerializerOptions() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });
        }

        /// <summary>Deep clone via JSON round-trip.</summary>
        /// <param name="self">The object to clone.</param>
        /// <typeparam name="T">The type of the object.</typeparam>
        /// <returns>Deep copy of the object.</returns>
        public static T DeepCopy<T>(this T self)
        {
            var serialized = JsonSerializer.Serialize(self);
            return JsonSerializer.Deserialize<T>(serialized);
        }

        /// <summary>
        /// Attempts to remove the value with the specified key from the dictionary.
        /// Returns true if the key was found and removed; otherwise, false.
        /// </summary>
        /// <typeparam name="TKey">The type of keys in the dictionary.</typeparam>
        /// <typeparam name="TValue">The type of values in the dictionary.</typeparam>
        /// <param name="dictionary">The dictionary to remove from.</param>
        /// <param name="key">The key to remove.</param>
        /// <returns>True if the key was found and removed; otherwise, false.</returns>
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
