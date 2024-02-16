using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace CallRecordInsights.Flattener
{
    public interface IJsonProcessor
    {
        IEnumerable<Dictionary<string, JsonNode?>> ProcessNode(JsonNode? jObject);
        IEnumerable<Dictionary<string, JsonNode?>> ProcessNode(string jsonString);
    }
}