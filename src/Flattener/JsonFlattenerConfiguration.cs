using CallRecordInsights.Extensions;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.Json.Nodes;

namespace CallRecordInsights.Flattener
{
    public class ConfigurationSorter<T> : IComparer<T> where T : IComparable<T>
    {
        private readonly IList<T> _sortedItems;

        public ConfigurationSorter(IList<T> sortedItems)
        {
            _sortedItems = sortedItems ?? throw new ArgumentNullException(nameof(sortedItems));
        }

        public int Compare(T? x, T? y)
        {
            if (x == null && y == null) { return 0; }
            if (x == null) { return -1; }
            if (y == null) { return 1; }

            var xIndex = _sortedItems.IndexOf(x);
            var yIndex = _sortedItems.IndexOf(y);
            // if both are not found, use the default comparer
            if (xIndex == -1 && yIndex == -1)
                return x.CompareTo(y);
            // if only one is found, it comes first
            if (yIndex == -1)
                return 1;
            if (xIndex == -1)
                return -1;
            // if both are found, use the index
            return xIndex.CompareTo(yIndex);
        }
    }

    public class JsonFlattenerConfiguration : IJsonFlattenerConfiguration
    {
        private readonly IDictionary<string, string> _configuration;

        public JsonNodeOptions Options { get; }

        public StringComparison StringComparison { get; }

        public JsonFlattenerConfiguration(FlattenerOptions options)
        {
            ArgumentNullException.ThrowIfNull(options, nameof(options));
            ArgumentNullException.ThrowIfNull(options.ColumnObjectMap, nameof(options.ColumnObjectMap));
            _configuration = new SortedDictionary<string, string>(
                options.ColumnObjectMap,
                new ConfigurationSorter<string>(
                    options.ColumnOrder?.Count == options.ColumnObjectMap.Count
                        ? options.ColumnOrder
                        : options.ColumnObjectMap.Keys.ToList()
                    ));
            StringComparison = options.CaseInsensitivePropertyNameMatching ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            Options = new JsonNodeOptions()
            {
                PropertyNameCaseInsensitive = options.CaseInsensitivePropertyNameMatching,
            };
            ValidateConfiguration();
        }

        internal JsonFlattenerConfiguration(FlattenerOptions options, bool shouldValidate)
        {
            ArgumentNullException.ThrowIfNull(options, nameof(options));
            ArgumentNullException.ThrowIfNull(options.ColumnObjectMap, nameof(options.ColumnObjectMap));
            _configuration = new SortedDictionary<string, string>(
                options.ColumnObjectMap,
                new ConfigurationSorter<string>(
                    options.ColumnOrder?.Count == options.ColumnObjectMap.Count
                        ? options.ColumnOrder
                        : options.ColumnObjectMap.Keys.ToList()
                    ));
            StringComparison = options.CaseInsensitivePropertyNameMatching ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            Options = new JsonNodeOptions()
            {
                PropertyNameCaseInsensitive = options.CaseInsensitivePropertyNameMatching,
            };
            if (shouldValidate)
                ValidateConfiguration();
        }

        /// <summary>
        /// Validates the configuration to ensure that no multiple expandable paths exist that are not on the same path.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        private void ValidateConfiguration()
        {
            var _configurationSelectorCache = _configuration.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ParseJsonPath());
            // if any possibleExpansions exist that are not on the same path, we have an invalid configuration, as Cartesian Products are not supported as of yet.
            var possiblyExpandableValues = new HashSet<string>();
            foreach (var kvp in _configurationSelectorCache)
            {
                var jsonPath = kvp.Value;
                var parentPath = jsonPath.GetParentPathAsEnumerable();
                if (parentPath == null || !parentPath.Any())
                    continue;

                if (jsonPath.IsExpandable())
                {
                    possiblyExpandableValues.Add(kvp.Key);
                    var expandsFrom = jsonPath.GetParentPathAsEnumerable();
                    while (expandsFrom.IsExpandable())
                        expandsFrom = expandsFrom.GetParentPathAsEnumerable();
                }
            }
            foreach (var exp in possiblyExpandableValues)
            {
                var expPath = _configurationSelectorCache[exp];
                var notAtRoot = expPath.GetParentPathAsEnumerable().LevelsOfExpansion() > 0;
                if (possiblyExpandableValues.Any(exp2 => exp != exp2
                        && !_configurationSelectorCache[exp2].GetCommonAncestorAsEnumerable(expPath, StringComparison).Any()
                        && (notAtRoot || _configurationSelectorCache[exp2].GetParentPathAsEnumerable().LevelsOfExpansion() > 0)))
                    throw new InvalidOperationException("Invalid configuration. All expandable paths must share the same common ancestor.\nCartesian Product is not supported at this time.");
            }
        }

        public string this[string key] => _configuration[key];

        public IEnumerable<string> Keys => _configuration.Keys;

        public IEnumerable<string> Values => _configuration.Values;

        public int Count => _configuration.Count;

        public bool ContainsKey(string key) => _configuration.ContainsKey(key);

        public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _configuration.GetEnumerator();

        public bool TryGetValue(string key, [MaybeNullWhen(false)] out string value) => _configuration.TryGetValue(key, out value);

        IEnumerator IEnumerable.GetEnumerator() => _configuration.GetEnumerator();
    }
}
