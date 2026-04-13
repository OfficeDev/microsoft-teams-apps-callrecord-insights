using CallRecordInsights.Extensions;
using FluentAssertions;
using Microsoft.Graph.Models.CallRecords;

namespace CallRecordInsights.Functions.Tests;

public class GraphSerializationTests
{
    [Fact]
    public void ClientUserAgent_SerializesAzureADAppId_WithCapitalAD()
    {
        // The flattener JSON path uses "azureAdAppId" (lowercase d)
        // but the SDK serializes as "azureADAppId" (uppercase D).
        // With CaseInsensitivePropertyNameMatching = false, this is a mismatch.
        var ua = new ClientUserAgent { AzureADAppId = "test-id" };
        var json = ua.SerializeAsString();

        json.Should().Contain("\"azureADAppId\"",
            "SDK serializes with capital 'AD' — the flattener path 'azureAdAppId' won't match case-sensitively");
    }
}
