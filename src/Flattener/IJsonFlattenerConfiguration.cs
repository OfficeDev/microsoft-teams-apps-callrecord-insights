using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace CallRecordInsights.Flattener
{
    public interface IJsonFlattenerConfiguration : IReadOnlyDictionary<string, string>
    {
        JsonNodeOptions Options { get; }
        StringComparison StringComparison { get; }
    }
}