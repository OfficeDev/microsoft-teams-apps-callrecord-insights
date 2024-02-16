using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CallRecordInsights.Models
{
    public class GraphNotificationBatch
    {
        [JsonPropertyName("value")]
        public IEnumerable<GraphEventNotification> Value { get; set; }
    }
}