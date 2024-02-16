using Microsoft.Graph.Models;
using System;
using System.Text.Json.Serialization;

namespace CallRecordInsights.Models
{
    public class GraphEventNotification
    {
        [JsonPropertyName("subscriptionId")]
        public string SubscriptionId { get; set; }

        [JsonPropertyName("clientState")]
        public string ClientState { get; set; }

        [JsonPropertyName("changeType")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ChangeType ChangeType { get; set; }

        [JsonPropertyName("resource")]
        public string Resource { get; set; }

        [JsonPropertyName("subscriptionExpirationDateTime")]
        public DateTimeOffset SubscriptionExpirationDateTime { get; set; }

        [JsonPropertyName("resourceData")]
        public EventNotificationResourceData ResourceData { get; set; }

        [JsonPropertyName("tenantId")]
        public string TenantId { get; set; }
    }
}