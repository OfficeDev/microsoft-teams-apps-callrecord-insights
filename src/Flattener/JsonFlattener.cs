using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using CallRecordInsights.Extensions;

namespace CallRecordInsights.Flattener
{
    public class JsonFlattener : IJsonProcessor
    {
        private readonly IJsonFlattenerConfiguration _configuration;
        public JsonFlattener(IJsonFlattenerConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration, nameof(configuration));
            _configuration = configuration;
        }

        /// <summary>
        /// Get a new row with all columns set to the same value as the parent, or null if missing.
        /// </summary>
        /// <param name="parent">The parent dictionary containing the column names and values.</param>
        /// <returns>A new row dictionary with all columns set to the same value as the parent, or null if missing.</returns>
        private Dictionary<string, JsonNode?> GetNewRow(IDictionary<string, JsonNode?>? parent = default)
        {
            var row = new Dictionary<string, JsonNode?>();
            foreach (var kvp in _configuration)
            {
                if (parent != null && parent.TryGetValue(kvp.Key, out var value))
                {
                    row.Add(kvp.Key, value);
                    continue;
                }
                row.Add(kvp.Key, null);
            }
            return row;
        }

        /// <summary>
        /// Process the json string and return a list of dictionaries with the column names and values
        /// </summary>
        /// <param name="jsonString">The JSON string to process.</param>
        /// <returns>A list of dictionaries with the column names and values.</returns>
        public IEnumerable<Dictionary<string, JsonNode?>> ProcessNode(string jsonString)
        {
            var jObject = JsonNode.Parse(jsonString, _configuration.Options);
            return ProcessNode(jObject);
        }

        /// <summary>
        /// Process the json node and return a list of dictionaries with the column names and values
        /// </summary>
        /// <param name="jObject">The JSON node to process.</param>
        /// <returns>A list of dictionaries with the column names and values.</returns>
        public IEnumerable<Dictionary<string, JsonNode?>> ProcessNode(JsonNode? jObject)
        {
            if (jObject == null)
                yield break;

            Dictionary<string, List<JsonNode?>> expanded = [];
            HashSet<string> potentialGroupingPaths = [];

            foreach (var kvp in _configuration)
            {
                var tokens = jObject.SelectTokens(kvp.Value).ToList();
                expanded[kvp.Key] = tokens;
                foreach (var node in tokens)
                {
                    if (node is null) continue;
                    potentialGroupingPaths.Add(node.GetPath().GetClosestExpansion().ToString());
                }
            }

            foreach (var path in potentialGroupingPaths)
            {
                if (potentialGroupingPaths.Any(c => path.IsParentOf(c)))
                    continue;

                var row = GetNewRow();
                foreach (var column in expanded.Keys)
                    row[column] = expanded[column].FirstOrDefault(v => v?.GetPath().IsRelativeOf(path) ?? false);

                yield return row;
            }
        }
    }
}
