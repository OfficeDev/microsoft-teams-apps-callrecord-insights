using System.Text.Json.Serialization;

namespace CallRecordInsights.Models
{
    public class EventNotificationResourceData
    {
        private const string ODATA_TYPE = "#microsoft.graph.callrecord";
        [JsonPropertyName("@odata.type")]
        public string ODataType { get; set; }

        [JsonPropertyName("@odata.id")]
        public string ODataId { get; set; }

        [JsonPropertyName("id")]
        public string Id { get; set; }

        /// <summary>
        /// Determines if the event notification is a call record event
        /// </summary>
        /// <returns></returns>
        public bool IsCallRecordEvent() => ODATA_TYPE.Equals(ODataType, System.StringComparison.OrdinalIgnoreCase);
    }
}