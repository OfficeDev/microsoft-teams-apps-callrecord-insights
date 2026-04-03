using Microsoft.Graph.Models.CallRecords;
using Microsoft.Kiota.Abstractions.Serialization;
using Microsoft.Kiota.Serialization.Json;

ApiClientBuilder.RegisterDefaultSerializer<JsonSerializationWriterFactory>();
ApiClientBuilder.RegisterDefaultDeserializer<JsonParseNodeFactory>();

var ua = new ClientUserAgent();
ua.AzureADAppId = "test-id";
var json = KiotaJsonSerializer.SerializeAsString(ua);
Console.WriteLine(json);
