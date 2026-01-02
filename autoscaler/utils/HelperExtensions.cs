using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace poolautoscaler.utils
{
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
    }
}
