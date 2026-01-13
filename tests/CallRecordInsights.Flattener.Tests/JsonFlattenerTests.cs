using CallRecordInsights.Extensions;
using CallRecordInsights.Flattener;
using CallRecordInsights.Models;
using FluentAssertions;
using System.Text.Json.Nodes;
using Xunit;

namespace CallRecordInsights.Tests;

public class JsonFlattenerTests
{
    private readonly JsonFlattener _flattener;
    private readonly string _minimalCallRecordJson;

    public JsonFlattenerTests()
    {
        _flattener = new JsonFlattener(IKustoCallRecordHelpers.DefaultConfiguration);
        _minimalCallRecordJson = File.ReadAllText("TestData/minimal-callrecord.json");
    }

    [Fact]
    public void ProcessNode_WithMinimalCallRecord_ReturnsAtLeastOneRow()
    {
        // Act
        var results = _flattener.ProcessNode(_minimalCallRecordJson).ToList();

        // Assert
        results.Should().NotBeEmpty("flattening a valid call record should produce at least one row");
        results.Should().HaveCountGreaterThan(0);
    }

    [Fact]
    public void ProcessNode_WithMinimalCallRecord_ContainsCallId()
    {
        // Act
        var results = _flattener.ProcessNode(_minimalCallRecordJson).ToList();

        // Assert
        var firstRow = results.First();
        firstRow.Should().ContainKey(nameof(IKustoCallRecord.CallId));
        firstRow[nameof(IKustoCallRecord.CallId)]?.GetValue<string>()
            .Should().Be("00000000-0000-0000-0000-000000000001");
    }

    [Fact]
    public void ProcessNode_WithMinimalCallRecord_ExpandsStreamsProperly()
    {
        // The minimal call record has:
        // 1 session -> 1 segment -> 1 media -> 1 stream
        // So we expect exactly 1 flattened row

        // Act
        var results = _flattener.ProcessNode(_minimalCallRecordJson).ToList();

        // Assert
        results.Should().HaveCount(1, "with 1 stream, we should get 1 flattened row");
    }

    [Fact]
    public void ProcessNode_WithNullInput_ReturnsEmptyEnumerable()
    {
        // Act
        var results = _flattener.ProcessNode((JsonNode?)null).ToList();

        // Assert
        results.Should().BeEmpty("null input should produce no rows");
    }

    [Fact]
    public void ProcessNode_WithEmptyObject_ReturnsEmptyEnumerable()
    {
        // Arrange
        var emptyJson = "{}";

        // Act
        var results = _flattener.ProcessNode(emptyJson).ToList();

        // Assert
        // With no arrays to expand and no potential grouping paths, we get zero rows
        results.Should().BeEmpty("empty object with no expansion points produces no rows");
    }

    [Fact]
    public void ProcessNode_InheritsParentValues_WhenExpandingArrays()
    {
        // All rows should inherit the CallId from the parent object

        // Act
        var results = _flattener.ProcessNode(_minimalCallRecordJson).ToList();

        // Assert
        foreach (var row in results)
        {
            row.Should().ContainKey(nameof(IKustoCallRecord.CallId));
            row[nameof(IKustoCallRecord.CallId)]?.GetValue<string>()
                .Should().Be("00000000-0000-0000-0000-000000000001",
                    "all rows should inherit the CallId from parent");
        }
    }

    [Fact]
    public void ProcessNode_WithMultipleStreams_CreatesMultipleRows()
    {
        // Arrange - Create JSON with multiple streams
        var multiStreamJson = @"{
            ""id"": ""test-call-id"",
            ""sessions"": [{
                ""id"": ""session-1"",
                ""segments"": [{
                    ""media"": [{
                        ""streams"": [
                            { ""streamId"": ""stream-1"", ""streamDirection"": ""callerToCallee"" },
                            { ""streamId"": ""stream-2"", ""streamDirection"": ""calleeToCaller"" }
                        ]
                    }]
                }]
            }]
        }";

        // Act
        var results = _flattener.ProcessNode(multiStreamJson).ToList();

        // Assert
        results.Should().HaveCount(2, "with 2 streams, we should get 2 flattened rows");

        var streamIds = results.Select(r => r[nameof(IKustoCallRecord.StreamId)]?.GetValue<string>()).ToList();
        streamIds.Should().Contain("stream-1");
        streamIds.Should().Contain("stream-2");
    }

    [Fact]
    public void ProcessNode_WithMultipleSessionsAndStreams_ExpandsCartesianCorrectly()
    {
        // Arrange - 2 sessions × 1 segment × 1 media × 2 streams each = 4 rows
        var complexJson = @"{
            ""id"": ""test-call-id"",
            ""sessions"": [
                {
                    ""id"": ""session-1"",
                    ""segments"": [{
                        ""media"": [{
                            ""streams"": [
                                { ""streamId"": ""s1-stream-1"" },
                                { ""streamId"": ""s1-stream-2"" }
                            ]
                        }]
                    }]
                },
                {
                    ""id"": ""session-2"",
                    ""segments"": [{
                        ""media"": [{
                            ""streams"": [
                                { ""streamId"": ""s2-stream-1"" },
                                { ""streamId"": ""s2-stream-2"" }
                            ]
                        }]
                    }]
                }
            ]
        }";

        // Act
        var results = _flattener.ProcessNode(complexJson).ToList();

        // Assert
        results.Should().HaveCount(4, "2 sessions with 2 streams each = 4 total rows");

        // Verify each session's streams are present
        var streamIds = results.Select(r => r[nameof(IKustoCallRecord.StreamId)]?.GetValue<string>()).ToList();
        streamIds.Should().Contain("s1-stream-1");
        streamIds.Should().Contain("s1-stream-2");
        streamIds.Should().Contain("s2-stream-1");
        streamIds.Should().Contain("s2-stream-2");
    }

    [Fact]
    public void ProcessNode_WithMissingArrays_HandlesGracefully()
    {
        // Arrange - Call record with no sessions array
        var noSessionsJson = @"{
            ""id"": ""test-call-id"",
            ""type"": ""groupCall""
        }";

        // Act
        var results = _flattener.ProcessNode(noSessionsJson).ToList();

        // Assert
        // Should still produce a row with the available data
        results.Should().HaveCount(1);
        results.First()[nameof(IKustoCallRecord.CallId)]?.GetValue<string>()
            .Should().Be("test-call-id");
        results.First()[nameof(IKustoCallRecord.SessionId)].Should().BeNull();
    }
}
