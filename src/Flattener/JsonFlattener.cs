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
        /// Get a new row with all columns set to null
        /// </summary>
        /// <param name="parent"></param>
        /// <returns></returns>
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
        /// <param name="jsonString"></param>
        /// <returns></returns>
        public IEnumerable<Dictionary<string, JsonNode?>> ProcessNode(string jsonString)
        {
            var jObject = JsonNode.Parse(jsonString, _configuration.Options);
            return ProcessNode(jObject);
        }

        /// <summary>
        /// Process the json node and return a list of dictionaries with the column names and values
        /// </summary>
        /// <param name="jObject"></param>
        /// <returns></returns>
        public IEnumerable<Dictionary<string, JsonNode?>> ProcessNode(JsonNode? jObject)
        {
            if (jObject == null)
            {
                return Enumerable.Empty<Dictionary<string, JsonNode?>>();
            }

            var result = new List<Dictionary<string, JsonNode?>>();
            var expanded = new Dictionary<string, List<JsonNode?>>();
            var currentIndices = new Dictionary<string, int>();
            foreach (var kvp in _configuration)
            {
                var columnName = kvp.Key;
                var jsonPath = kvp.Value;
                expanded[columnName] = jObject.SelectTokens(jsonPath).ToList();
                currentIndices[columnName] = 0;
            }

            var expandableNodes = expanded.Where(kvp => _configuration[kvp.Key].IsExpandable());
            var MaxDepth = expandableNodes?.Max(kvp => kvp.Value.FirstOrDefault()?.GetPath().LevelsOfExpansion() ?? 0) ?? 0;
            var LeafNodes = expandableNodes?.Where(kvp => kvp.Value.FirstOrDefault()?.GetPath().LevelsOfExpansion() == MaxDepth)
                .ToList();
            if (LeafNodes is null || LeafNodes.Count == 0)
            {
                var row = GetNewRow();
                foreach (var column in expanded.Keys)
                {
                    row[column] = expanded[column].FirstOrDefault();
                }
                result.Add(row);
                return result;
            }
            var RowCount = LeafNodes.Max(kvp => kvp.Value?.Count ?? 0);
            var nonLeafKeys = expanded.Keys.Except(LeafNodes.Select(kvp => kvp.Key)).ToList();
            for (var i = 0; i < RowCount; i++)
            {
                var row = GetNewRow();
                var leafPath = string.Empty;
                foreach (var leaf in LeafNodes)
                {
                    JsonNode? value = default;
                    if (leaf.Value.Count == RowCount)
                    {
                        value = leaf.Value[i];
                        currentIndices[leaf.Key] = i;
                    }
                    else if (leaf.Value.Count > 0)
                    {
                        for (var j = currentIndices[leaf.Key]; j < leaf.Value.Count; j++)
                        {
                            var path = leaf.Value[j]?.GetPath();
                            if (path is null)
                                continue;
                            if (path.IsRelativeOf(leafPath))
                            {
                                value = leaf.Value[j];
                                currentIndices[leaf.Key] = j;
                                break;
                            }
                        }   
                    }

                    row[leaf.Key] = value;

                    if (value != default && leafPath == string.Empty && leaf.Value.Count > 0)
                    {
                        leafPath = value.GetPath().ToJsonPath().ToString();
                    }
                }
                // TODO: Move this out of the for loop RowCount
                foreach (var column in nonLeafKeys)
                {
                    for (var j = currentIndices[column]; j < expanded[column].Count; j++)
                    {
                        var path = expanded[column][j]?.GetPath();
                        if (path is null)
                            continue;
                        if (path.IsRelativeOf(leafPath))
                        {
                            row[column] = expanded[column][j];
                            currentIndices[column] = j;
                            break;
                        }
                    }
                }
                result.Add(row);
            }
            return result;
        }
    }
}
