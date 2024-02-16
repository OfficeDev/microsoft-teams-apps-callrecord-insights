using System.Text.Json.Serialization;

namespace CallRecordInsights.Models
{
    public class CosmosEventNotification
    {
        public CosmosEventNotification(GraphEventNotification notification)
        {
            Id = notification.ResourceData.Id;
            TenantId = notification.TenantId;
            ODataType = notification.ResourceData.ODataType;
            ODataId = notification.ResourceData.ODataId;
        }

        public CosmosEventNotification() { }

        public string Id { get; set; }

        public string TenantId { get; set; }

        [JsonPropertyName("@odata.id")]
        public string ODataId { get; set; }

        [JsonPropertyName("@odata.type")]
        public string ODataType { get; set; }

        /// <summary>
        /// Gets the queue string representation of the notification
        /// This is in the format of <see cref="TenantId"/>|<see cref="Id"/>
        /// </summary>
        /// <returns></returns>
        public string GetQueueString() => $"{TenantId}|{Id}";
    }
}
