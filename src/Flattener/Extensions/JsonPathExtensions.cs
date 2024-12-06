using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CallRecordInsights.Extensions
{
    public static class JsonPathExtensions
    {
        /// <summary>
        /// Selects a single token from the provided node using the provided JSON path.
        /// </summary>
        /// <param name="node"></param>
        /// <param name="jsonPath"></param>
        /// <returns></returns>
        public static JsonNode? SelectToken(this JsonNode node, ReadOnlySpan<char> jsonPath)
        {
            var tokens = node.SelectTokens(jsonPath);
            return tokens.FirstOrDefault();
        }

        /// <summary>
        /// Selects all tokens from the provided node using the provided JSON path.
        /// </summary>
        /// <param name="node"></param>
        /// <param name="jsonPath"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public static IEnumerable<JsonNode?> SelectTokens(this JsonNode node, ReadOnlySpan<char> jsonPath)
        {
            var selectors = ParseSelectors(jsonPath).ToList();
            var tokens = new List<JsonNode?>() { node };

            foreach (var selector in selectors)
            {
                tokens = selector switch
                {
                    ObjectPropertySelector propertySelector => SelectTokensForPropertySelector(tokens, propertySelector).ToList(),
                    ArrayWildcardSelector wildcardSelector => SelectTokensForArrayWildcardSelector(tokens, wildcardSelector).ToList(),
                    ArrayIndexSelector indexSelector => SelectTokensForArrayIndexSelector(tokens, indexSelector).ToList(),
                    ArraySliceSelector sliceSelector => SelectTokensForArraySliceSelector(tokens, sliceSelector).ToList(),
                    _ => throw new InvalidOperationException($"Invalid selector type: {selector.GetType()}"),
                };
            }

            return tokens;
        }

        private static readonly Regex JsonPathSeparator = new("(?<!['\"][^'\"\\.]+)\\.", RegexOptions.Compiled | RegexOptions.ExplicitCapture);

        /// <summary>
        /// Gets the ranges of the selectors in the provided JSON path.
        /// </summary>
        /// <param name="jsonPath"></param>
        /// <returns></returns>
        private static IReadOnlyList<Range> GetSelectorRanges(ReadOnlySpan<char> jsonPath)
        {
            var selectors = new List<Range>();
            if (!jsonPath.Contains('.'))
            {
                selectors.Add(new(0, jsonPath.Length));
                return selectors;
            }
            var index = 0;
            if (jsonPath.Contains('\'') || jsonPath.Contains('"'))
            {
                var parts = JsonPathSeparator.Split(jsonPath.ToString());
                foreach (var part in parts)
                {
                    if (part.Length == 0)
                        continue;
                    selectors.Add(new(index, index + part.Length));
                    index += part.Length + 1;
                }
                return selectors;
            }
            while (index < jsonPath.Length)
            {
                var start = index;
                while (index < jsonPath.Length && jsonPath[index] != '.')
                {
                    index++;
                }
                if (index - start > 0)
                    selectors.Add(new(start, index));
                index++;
            }
            return selectors;
        }

        /// <summary>
        /// Parses the provided JSON path into a list of selectors.
        /// </summary>
        /// <param name="jsonPath"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        private static IEnumerable<ISelector> ParseSelectors(ReadOnlySpan<char> jsonPath)
        {
            var selectors = new List<ISelector>();
            foreach (var range in GetSelectorRanges(jsonPath))
            {
                var selector = jsonPath[range];
                if (selector == null || selector.Length == 0 || (selector.Length == 1 && selector[0] == '$'))
                    continue;

                if (selector[^1] == ']')
                {
                    var openBracketIndex = selector.LastIndexOf('[');
                    if (openBracketIndex == -1)
                        throw new InvalidOperationException($"Invalid selector: {selector}");

                    var prefix = selector[..openBracketIndex];
                    var indexOrWildcard = selector.Slice(openBracketIndex + 1, selector.Length - openBracketIndex - 2);
                    if (prefix.Length > 0)
                        selectors.Add(new ObjectPropertySelector(prefix));
                    if (indexOrWildcard.Length == 0 || (indexOrWildcard.Length == 1 && indexOrWildcard[0] == '*'))
                        selectors.Add(new ArrayWildcardSelector());
                    else if (indexOrWildcard.IndexOf(':') != -1)
                        selectors.Add(ArraySliceSelector.Parse(indexOrWildcard));
                    else if (int.TryParse(indexOrWildcard, out var index))
                        selectors.Add(new ArrayIndexSelector(index));
                    else
                        selectors.Add(new ObjectPropertySelector(indexOrWildcard.Trim("'\"")));

                    continue;
                }

                selectors.Add(new ObjectPropertySelector(selector));
            }
            return selectors;
        }

        /// <summary>
        /// Selects all tokens from all provided nodes using the provided JSON path.
        /// </summary>
        /// <param name="tokens"></param>
        /// <param name="selector"></param>
        /// <returns></returns>
        private static IEnumerable<JsonNode?> SelectTokensForPropertySelector(IEnumerable<JsonNode?> tokens, ObjectPropertySelector selector)
        {
            return tokens.SelectMany(token =>
            {
                if (token is JsonObject jsonObject && jsonObject.TryGetPropertyValue(selector.PropertyName, out var propertyValue))
                {
                    if (propertyValue is JsonArray array)
                    {
                        return array;
                    }

                    return new[] { propertyValue };
                }

                return Enumerable.Empty<JsonNode?>();
            });
        }

        /// <summary>
        /// Selects all tokens from all provided nodes using the provided JSON path.
        /// </summary>
        /// <param name="tokens"></param>
        /// <param name="wildcardSelector"></param>
        /// <returns></returns>
        private static IEnumerable<JsonNode?> SelectTokensForArrayWildcardSelector(IEnumerable<JsonNode?> tokens, ArrayWildcardSelector wildcardSelector)
        {
            return tokens;
        }

        /// <summary>
        /// Selects all tokens from all provided nodes using the provided JSON path.
        /// </summary>
        /// <param name="tokens"></param>
        /// <param name="selector"></param>
        /// <returns></returns>
        private static IEnumerable<JsonNode?> SelectTokensForArrayIndexSelector(IEnumerable<JsonNode?> tokens, ArrayIndexSelector selector)
        {
            var index = selector.Index;
            if ((index.Value > 0 || index.Value == 0 && !index.IsFromEnd) && index.Value < tokens.Count())
                return new[] { tokens.ElementAt(index.Value) };

            return Enumerable.Empty<JsonNode?>();
        }

        /// <summary>
        /// Selects all tokens from all provided nodes using the provided JSON path.
        /// </summary>
        /// <param name="tokens"></param>
        /// <param name="selector"></param>
        /// <returns></returns>
        private static IEnumerable<JsonNode?> SelectTokensForArraySliceSelector(IEnumerable<JsonNode?> tokens, ArraySliceSelector selector)
        {
            var selected = tokens.Take(selector.Range).ToList();
            if (selector.Step == 1)
                return selected;

            var selectedItems = new List<JsonNode?>();
            for (var i = 0; i < selected.Count; i += selector.Step)
            {
                selectedItems.Add(selected[i]);
            }
            return selectedItems;
        }

        /// <summary>
        /// Parses the provided JSON path into a list of selectors.
        /// </summary>
        /// <param name="jsonPath"></param>
        /// <returns></returns>
        public static IEnumerable<ISelector> ParseJsonPath(this string jsonPath) => ParseJsonPath(jsonPath.AsSpan());
        public static IEnumerable<ISelector> ParseJsonPath(this ReadOnlySpan<char> jsonPath) => ParseSelectors(jsonPath);

        /// <summary>
        /// Determines whether the provided JSON path is a parent of the provided child JSON path.
        /// </summary>
        /// <param name="parentPath"></param>
        /// <param name="childPath"></param>
        /// <param name="comparisonType"></param>
        /// <returns></returns>
        public static bool IsParentOf(this string parentPath, string childPath, StringComparison comparisonType = StringComparison.Ordinal) => IsParentOf(parentPath.AsSpan(), childPath.AsSpan(), comparisonType);
        public static bool IsParentOf(this ReadOnlySpan<char> parentPath, ReadOnlySpan<char> childPath, StringComparison comparisonType = StringComparison.Ordinal) => IsParentOf(ParseJsonPath(parentPath), ParseJsonPath(childPath), comparisonType);
        public static bool IsParentOf(this IEnumerable<ISelector> parentPath, IEnumerable<ISelector> childPath, StringComparison comparisonType = StringComparison.Ordinal)
        {
            var parentSelectors = parentPath.ToList();
            var childSelectors = childPath.ToList();

            if (parentSelectors.Count >= childSelectors.Count)
                return false;

            for (var i = 0; i < parentSelectors.Count; i++)
            {
                // if they are not of the same type, they are not the same
                if (parentSelectors[i] is IArraySelector && childSelectors[i] is not IArraySelector)
                    return false;

                if (parentSelectors[i] is not IArraySelector && childSelectors[i] is IArraySelector)
                    return false;

                if (parentSelectors[i] is ArrayIndexSelector parentIndexSelector)
                {
                    if (childSelectors[i] is ArrayIndexSelector childIndexSelector && !parentIndexSelector.Equals(childIndexSelector))
                        return false;

                    if (childSelectors[i] is ArraySliceSelector childSliceSelector && 
                        (childSliceSelector.Start.Value > parentIndexSelector.Index.Value || childSliceSelector.End.Value < parentIndexSelector.Index.Value))
                        return false;
                }

                if (parentSelectors[i] is ObjectPropertySelector parentPropertySelector && childSelectors[i] is ObjectPropertySelector childPropertySelector
                    && !parentPropertySelector.Equals(childPropertySelector, comparisonType))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Determines whether the provided JSON paths are at the same level in the JSON structure.
        /// </summary>
        /// <param name="sourcePath"></param>
        /// <param name="targetPath"></param>
        /// <param name="comparisonType"></param>
        /// <returns></returns>
        public static bool IsSiblingOf(this string sourcePath, string targetPath, StringComparison comparisonType = StringComparison.Ordinal) => IsSiblingOf(sourcePath.AsSpan(), targetPath.AsSpan(), comparisonType);
        public static bool IsSiblingOf(this ReadOnlySpan<char> sourcePath, ReadOnlySpan<char> targetPath, StringComparison comparisonType = StringComparison.Ordinal) => IsSiblingOf(ParseJsonPath(sourcePath), ParseJsonPath(targetPath), comparisonType);
        public static bool IsSiblingOf(this IEnumerable<ISelector> sourcePath, IEnumerable<ISelector> targetPath, StringComparison comparisonType = StringComparison.Ordinal)
        {
            var sourceSelectors = sourcePath.ToList();
            var targetSelectors = targetPath.ToList();

            if (sourceSelectors.Count != targetSelectors.Count)
                return false;

            for (var i = 0; i < sourceSelectors.Count; i++)
            {
                // if they are not of the same type, they are not the same
                if (sourceSelectors[i] is IArraySelector && targetSelectors[i] is not IArraySelector)
                    return false;

                if (sourceSelectors[i] is not IArraySelector && targetSelectors[i] is IArraySelector)
                    return false;

                if (sourceSelectors[i] is ObjectPropertySelector sourcePropertySelector && targetSelectors[i] is ObjectPropertySelector targetPropertySelector
                    && !sourcePropertySelector.Equals(targetPropertySelector, comparisonType))
                    return false;
            }

            if (MemoryExtensions.Equals(sourcePath.GetParentPath(), targetPath.GetParentPath(), comparisonType))
            {
                if (sourceSelectors[^1] is ObjectPropertySelector sourcePropertySelector && targetSelectors[^1] is ObjectPropertySelector targetPropertySelector)
                    return !sourcePropertySelector.Equals(targetPropertySelector, comparisonType);

                return true;
            }

            return false;
        }

        /// <summary>
        /// Determines whether the provided JSON paths are on the same unique parent path in the JSON structure.
        /// </summary>
        /// <param name="sourcePath"></param>
        /// <param name="targetPath"></param>
        /// <param name="comparisonType"></param>
        /// <returns></returns>
        public static bool IsRelativeOf(this string sourcePath, string targetPath, StringComparison comparisonType = StringComparison.Ordinal) => IsRelativeOf(sourcePath.AsSpan(), targetPath.AsSpan(), comparisonType);
        public static bool IsRelativeOf(this ReadOnlySpan<char> sourcePath, ReadOnlySpan<char> targetPath, StringComparison comparisonType = StringComparison.Ordinal) => IsRelativeOf(ParseJsonPath(sourcePath), ParseJsonPath(targetPath), comparisonType);
        public static bool IsRelativeOf(this IEnumerable<ISelector> potentialRelative, IEnumerable<ISelector> otherPotentialRelative, StringComparison comparisonType = StringComparison.Ordinal)
        {
            var potentialExpansion = GetClosestExpansionAsEnumerable(potentialRelative);
            var otherPotentialExpansion = GetClosestExpansionAsEnumerable(otherPotentialRelative);
            foreach ((var potential, var other) in potentialExpansion.Zip(otherPotentialExpansion))
            {
                if (potential is IArraySelector && other is not IArraySelector)
                    return false;

                if (potential is not IArraySelector && other is IArraySelector)
                    return false;

                if (potential is ObjectPropertySelector potentialPropertySelector
                        && other is ObjectPropertySelector otherPotentialPropertySelector
                        && !potentialPropertySelector.Equals(otherPotentialPropertySelector, comparisonType))
                    return false;

                if (potential.ToString() != other.ToString())
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Gets the closest expandable ancestor of the provided JSON path.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static ReadOnlySpan<char> GetClosestExpansion(this string path) => GetClosestExpansion(path.AsSpan());
        public static ReadOnlySpan<char> GetClosestExpansion(this ReadOnlySpan<char> path) => GetClosestExpansion(ParseJsonPath(path));
        public static ReadOnlySpan<char> GetClosestExpansion(this IEnumerable<ISelector> path) => ToJsonPath(GetClosestExpansionAsEnumerable(path));
        public static IEnumerable<ISelector> GetClosestExpansionAsEnumerable(this IEnumerable<ISelector> path)
        {
            var pathArray = path.ToList();
            var i = pathArray.Count - 1;
            while (i >= 0 && pathArray[i] is not IArraySelector)
            {
                i--;
            }
            return path.Take(i + 1);
        }

        /// <summary>
        /// Gets the number of levels of expansion in the provided JSON path.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static int LevelsOfExpansion(this string path) => LevelsOfExpansion(path.AsSpan());
        public static int LevelsOfExpansion(this ReadOnlySpan<char> path) => LevelsOfExpansion(ParseJsonPath(path));
        public static int LevelsOfExpansion(this IEnumerable<ISelector> path) => path.Count(s => s is IArraySelector);

        /// <summary>
        /// Finds the closest common ancestor of the provided JSON paths.
        /// </summary>
        /// <param name="sourcePath"></param>
        /// <param name="targetPath"></param>
        /// <param name="comparisonType"></param>
        /// <returns></returns>
        public static ReadOnlySpan<char> GetCommonAncestor(this string sourcePath, string targetPath, StringComparison comparisonType = StringComparison.Ordinal) => GetCommonAncestor(sourcePath.AsSpan(), targetPath.AsSpan(), comparisonType);
        public static ReadOnlySpan<char> GetCommonAncestor(this string sourcePath, IEnumerable<ISelector> targetPath, StringComparison comparisonType = StringComparison.Ordinal) => GetCommonAncestor(sourcePath.AsSpan(), targetPath, comparisonType);
        public static ReadOnlySpan<char> GetCommonAncestor(this IEnumerable<ISelector> sourcePath, string targetPath, StringComparison comparisonType = StringComparison.Ordinal) => GetCommonAncestor(sourcePath, targetPath.AsSpan(), comparisonType);
        public static ReadOnlySpan<char> GetCommonAncestor(this ReadOnlySpan<char> sourcePath, ReadOnlySpan<char> targetPath, StringComparison comparisonType = StringComparison.Ordinal) => GetCommonAncestor(ParseJsonPath(sourcePath), ParseJsonPath(targetPath), comparisonType);
        public static ReadOnlySpan<char> GetCommonAncestor(this ReadOnlySpan<char> sourcePath, IEnumerable<ISelector> targetPath, StringComparison comparisonType = StringComparison.Ordinal) => GetCommonAncestor(ParseJsonPath(sourcePath), targetPath, comparisonType);
        public static ReadOnlySpan<char> GetCommonAncestor(this IEnumerable<ISelector> sourcePath, ReadOnlySpan<char> targetPath, StringComparison comparisonType = StringComparison.Ordinal) => GetCommonAncestor(sourcePath, ParseJsonPath(targetPath), comparisonType);
        public static ReadOnlySpan<char> GetCommonAncestor(this IEnumerable<ISelector> sourcePath, IEnumerable<ISelector> targetPath, StringComparison comparisonType = StringComparison.Ordinal) => ToJsonPath(GetCommonAncestorAsEnumerable(sourcePath, targetPath, comparisonType));
        public static IEnumerable<ISelector> GetCommonAncestorAsEnumerable(this IEnumerable<ISelector> sourcePath, IEnumerable<ISelector> targetPath, StringComparison comparisonType = StringComparison.Ordinal)
        {
            var sourceSelectors = sourcePath.ToList();
            var targetSelectors = targetPath.ToList();

            var searchEnd = Math.Min(sourceSelectors.Count, targetSelectors.Count);

            var commonAncestor = new List<ISelector>();
            for (var i = 0; i < searchEnd; i++)
            {
                // if they are not of the same type, they are not the same
                if (sourceSelectors[i] is IArraySelector && targetSelectors[i] is not IArraySelector)
                    break;

                if (sourceSelectors[i] is not IArraySelector && targetSelectors[i] is IArraySelector)
                    break;

                if (sourceSelectors[i] is ObjectPropertySelector sourcePropertySelector
                    && targetSelectors[i] is ObjectPropertySelector targetPropertySelector
                    && !sourcePropertySelector.Equals(targetPropertySelector, comparisonType))
                    break;

                commonAncestor.Add(sourceSelectors[i]);
            }
            return commonAncestor;
        }

        /// <summary>
        /// Determines whether the provided JSON path is expandable.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static bool IsExpandable(this string path) => IsExpandable(path.AsSpan());
        public static bool IsExpandable(this ReadOnlySpan<char> path) => IsExpandable(ParseJsonPath(path));
        public static bool IsExpandable(this IEnumerable<ISelector> path) => path.Any(s => s is IArraySelector && s is not ArrayIndexSelector);

        /// <summary>
        /// Converts the provided JSON path to a string.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static ReadOnlySpan<char> ToJsonPath(this string path) => ToJsonPath(path.AsSpan());
        public static ReadOnlySpan<char> ToJsonPath(this ReadOnlySpan<char> path) => ToJsonPath(ParseJsonPath(path));
        public static ReadOnlySpan<char> ToJsonPath(this IEnumerable<ISelector> path)
        {
            var builder = new StringBuilder("$");
            foreach (var selector in path)
            {
                selector.AppendTo(builder);
            }
            return builder.ToString();
        }

        /// <summary>
        /// Gets the parent path of the provided JSON path.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static ReadOnlySpan<char> GetParentPath(this string path) => GetParentPath(path.AsSpan());
        public static ReadOnlySpan<char> GetParentPath(this ReadOnlySpan<char> path) => GetParentPath(ParseJsonPath(path));
        public static ReadOnlySpan<char> GetParentPath(this IEnumerable<ISelector> path) => ToJsonPath(GetParentPathAsEnumerable(path));
        public static IEnumerable<ISelector> GetParentPathAsEnumerable(this IEnumerable<ISelector> path)
        {
            return path.SkipLast(1);
        }
    }

    public interface ISelector
    {
        StringBuilder AppendTo(StringBuilder builder);
    }

    public interface IArraySelector : ISelector
    {
    }

    public class ArrayWildcardSelector : IArraySelector
    {
        public StringBuilder AppendTo(StringBuilder builder)
        {
            return builder.Append("[*]");
        }

        public override string ToString()
        {
            return "[*]";
        }

        public bool Equals(ArrayWildcardSelector? other)
        {
            return other is not null;
        }

        public override bool Equals(object? obj)
        {
            if (obj is not ArrayWildcardSelector other)
                return false;
            return Equals(other);
        }

        public override int GetHashCode()
        {
            return 309278523; // "[*]".GetHashCode();
        }
    }

    public class ArrayIndexSelector : IArraySelector
    {
        public Index Index { get; }
        private readonly int _index;

        public ArrayIndexSelector(int index)
        {
            _index = index;
            Index = index >= 0 ? Index.FromStart(index) : Index.FromEnd(-index);
        }

        public StringBuilder AppendTo(StringBuilder builder)
        {
            builder.Append('[');
            builder.Append(_index);
            builder.Append(']');
            return builder;
        }

        public override string ToString()
        {
            return AppendTo(new StringBuilder()).ToString();
        }

        public bool Equals(ArrayIndexSelector? other)
        {
            if (other is null)
                return false;
            return Index.Equals(other.Index);
        }

        public override bool Equals(object? obj)
        {
            if (obj is not ArrayIndexSelector other)
                return false;
            return Equals(other);
        }

        public override int GetHashCode()
        {
            return Index.GetHashCode();
        }
    }

    public class ArraySliceSelector : IArraySelector
    {
        public Index Start { get; }
        private readonly int? _start;

        public Index End { get; }
        private readonly int? _end;
        public int Step { get; }

        public Range Range => new(Start, End);

        public ArraySliceSelector(int? start, int? end, int step)
        {
            Start = start != null ? start >= 0 ? Index.FromStart(start.Value) : Index.FromEnd(-start.Value) : Index.Start;
            _start = start;
            End = end != null ? end >= 0 ? Index.FromStart(end.Value) : Index.FromEnd(-end.Value) : Index.End;
            _end = end;
            if (step <= 0)
                throw new InvalidOperationException("Step must be greater than zero");
            Step = step;
        }

        public StringBuilder AppendTo(StringBuilder builder)
        {
            return builder.Append('[')
                .Append(_start)
                .Append(':')
                .Append(_end)
                .Append(':')
                .Append(Step)
                .Append(']');
        }

        public override string ToString()
        {
            return AppendTo(new StringBuilder()).ToString();
        }

        public static ArraySliceSelector Parse(ReadOnlySpan<char> selector)
        {
            if (selector.Length == 0)
                throw new InvalidOperationException($"Invalid selector: {selector}");

            var partIndex = 0;
            var parts = new int?[3] {null, null, 1};

            var subSelector = selector;
            while (subSelector.Length > 0 && partIndex < parts.Length)
            {
                var nextIndex = subSelector.IndexOf(':');
                if (nextIndex == -1)
                {
                }
                if (nextIndex > 0 && int.TryParse(subSelector[..nextIndex], out var parsedInt))
                    parts[partIndex] = parsedInt;
                partIndex++;
                subSelector = subSelector[(nextIndex+1)..];
            }
            if (subSelector.Length > 0)
                throw new InvalidOperationException($"Invalid selector: {selector}");

            return new ArraySliceSelector(parts[0], parts[1], parts[2]!.Value);
        }
    
        public bool Equals(ArraySliceSelector? other)
        {
            if (other is null)
                return false;
            return Start.Equals(other.Start) && End.Equals(other.End) && Step == other.Step;
        }

        public override bool Equals(object? obj)
        {
            if (obj is not ArraySliceSelector other)
                return false;
            return Equals(other);
        }

        public override int GetHashCode()
        {
            return Start.GetHashCode() ^ End.GetHashCode() ^ Step.GetHashCode();
        }
    }

    public class ObjectPropertySelector : ISelector
    {
        public string PropertyName { get; }

        public ObjectPropertySelector(string propertyName)
        {
            PropertyName = propertyName;
        }

        public ObjectPropertySelector(ReadOnlySpan<char> propertyName)
        {
            PropertyName = propertyName.ToString();
        }

        public StringBuilder AppendTo(StringBuilder builder)
        {
            if (PropertyName.IndexOfAny(SpecialCharacters) != -1)
                return builder.Append("['")
                    .Append(PropertyName)
                    .Append("']");

            return builder.Append('.')
                .Append(PropertyName);
        }

        public override string ToString()
        {
            return AppendTo(new StringBuilder()).ToString();
        }

        public bool Equals(ObjectPropertySelector? other, StringComparison comparisonType = StringComparison.Ordinal)
        {
            if (other is null)
                return false;
            return string.Equals(PropertyName, other.PropertyName, comparisonType);
        }

        public override bool Equals(object? obj)
        {
            if (obj is not ObjectPropertySelector other)
                return false;
            return Equals(other);
        }

        public override int GetHashCode()
        {
            return PropertyName.GetHashCode();
        }

        private static readonly char[] SpecialCharacters = new char[18] { '.', ' ', '\'', '/', '"', '[', ']', '(', ')', '\t', '\n', '\r', '\f', '\b', '\\', '\u0085', '\u2028', '\u2029' };
    }
}
