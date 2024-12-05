using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Serialization;
using Microsoft.Kiota.Serialization.Json;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CallRecordInsights.Extensions
{
    public static class IParsableExtensions
    {
        private const string ContentType = "application/json";

        static IParsableExtensions()
        {
            ApiClientBuilder.RegisterDefaultDeserializer<JsonParseNodeFactory>();
        }

        /// <summary>
        /// Serializes a given <see cref="IParsable"/> object to a JSON string in a synchronous manner.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="item"></param>
        /// <returns></returns>
        public static string SerializeAsString<T>(this T item) where T : IParsable
        {
            using var streamReader = new StreamReader(item.SerializeAsUTF8Stream(), Encoding.UTF8);
            return streamReader.ReadToEnd();
        }

        /// <summary>
        /// Serializes a given <see cref="IParsable"/> object to a JSON string.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="item"></param>
        /// <returns></returns>
        public static async Task<string> SerializeAsStringAsync<T>(this T item) where T : IParsable
        {
            using var streamReader = new StreamReader(item.SerializeAsUTF8Stream(), Encoding.UTF8);
            return await streamReader.ReadToEndAsync();
        }

        /// <summary>
        /// Serializes a given <see cref="IEnumerable{IParsable}"/> collection to a JSON string in a synchronous manner.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="items"></param>
        /// <returns></returns>
        public static string SerializeAsString<T>(this IEnumerable<T> items) where T : IParsable
        {
            using var streamReader = new StreamReader(items.SerializeAsUTF8Stream(), Encoding.UTF8);
            return streamReader.ReadToEnd();
        }

        /// <summary>
        /// Serializes a given <see cref="IEnumerable{IParsable}"/> collection to a JSON string.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="items"></param>
        /// <returns></returns>
        public static async Task<string> SerializeAsStringAsync<T>(this IEnumerable<T> items) where T : IParsable
        {
            using var streamReader = new StreamReader(items.SerializeAsUTF8Stream(), Encoding.UTF8);
            return await streamReader.ReadToEndAsync();
        }

        /// <summary>
        /// Serializes a given <see cref="IDictionary{string, IParsable}"/> dictionary to a JSON string in a synchronous manner.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="dictionary"></param>
        /// <returns></returns>
        public static string SerializeAsString<T>(this IDictionary<string, T> dictionary) where T : IParsable
        {
            using var streamReader = new StreamReader(dictionary.SerializeAsUTF8Stream(), Encoding.UTF8);
            return streamReader.ReadToEnd();
        }

        /// <summary>
        /// Serializes a given <see cref="IDictionary{string, IParsable}"/> dictionary to a JSON string.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="dictionary"></param>
        /// <returns></returns>
        public static async Task<string> SerializeAsStringAsync<T>(this IDictionary<string, T> dictionary) where T : IParsable
        {
            using var streamReader = new StreamReader(dictionary.SerializeAsUTF8Stream(), Encoding.UTF8);
            return await streamReader.ReadToEndAsync();
        }

        /// <summary>
        /// Deserializes a given JSON string to a <see cref="IParsable"/> object in a synchronous manner.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="json"></param>
        /// <returns></returns>
        public static T? DeserializeObject<T>(this string json) where T : IParsable, new()
        {
            if (json == null)
                return default;
            return json.DeserializeObjectAsync<T>().ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Deserializes a given JSON string to a <see cref="IParsable"/> object in a synchronous manner.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="json"></param>
        /// <returns></returns>
        public static async Task<T?> DeserializeObjectAsync<T>(this string json) where T : IParsable, new()
        {
            var parsableFactory = ParseNodeFactoryRegistry.DefaultInstance;
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            var rootNode = await parsableFactory.GetRootParseNodeAsync(ContentType, stream);
            return rootNode.GetObjectValue((IParseNode item) => new T());
        }

        /// <summary>
        /// Deserializes a given JSON string to a <see cref="IParsable"/> object.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="json"></param>
        /// <returns></returns>
        public static IEnumerable<T> DeserializeCollection<T>(this string json) where T : IParsable, new()
        {
            if (json == null)
                return Enumerable.Empty<T>();
            return json.DeserializeCollectionAsync<T>().ConfigureAwait(false).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Deserializes a given JSON string to a <see cref="IParsable"/> object.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="json"></param>
        /// <returns></returns>
        public static async Task<IEnumerable<T>> DeserializeCollectionAsync<T>(this string json) where T : IParsable, new()
        {
            var parsableFactory = ParseNodeFactoryRegistry.DefaultInstance;
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            var rootNode = await parsableFactory.GetRootParseNodeAsync(ContentType, stream);
            return rootNode.GetCollectionOfObjectValues((IParseNode item) => new T());
        }

        /// <summary>
        /// Serializes a given <see cref="IParsable"/> object to a JSON UTF-8 stream in a synchronous manner.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="item"></param>
        /// <returns></returns>
        public static Stream SerializeAsUTF8Stream<T>(this T item) where T : IParsable
        {
            using var writer = new JsonSerializationWriter();
            writer.WriteObjectValue(null, item);
            return writer.GetSerializedContent();
        }

        /// <summary>
        /// Serializes a given <see cref="IEnumerable{IParsable}"/> collection to a JSON UTF-8 stream in a synchronous manner.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="items"></param>
        /// <returns></returns>
        public static Stream SerializeAsUTF8Stream<T>(this IEnumerable<T> items) where T : IParsable
        {
            using var writer = new JsonSerializationWriter();
            writer.WriteCollectionOfObjectValues(null, items);
            return writer.GetSerializedContent();
        }

        /// <summary>
        /// Serializes a given <see cref="IDictionary{string, IParsable}"/> dictionary to a JSON UTF-8 stream in a synchronous manner.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="dictionary"></param>
        /// <returns></returns>
        public static Stream SerializeAsUTF8Stream<T>(this IDictionary<string, T> dictionary) where T : IParsable
        {
            using var writer = new JsonSerializationWriter();
            writer.writer.WriteStartObject();
            foreach (var kvp in dictionary)
            {
                writer.WriteObjectValue(kvp.Key, kvp.Value);
            }
            writer.writer.WriteEndObject();
            return writer.GetSerializedContent();
        }
    }
}