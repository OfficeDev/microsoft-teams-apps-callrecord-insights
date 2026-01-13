using CallRecordInsights.Extensions;
using CallRecordInsights.Flattener;
using CallRecordInsights.Models;
using FluentAssertions;
using Xunit;

namespace CallRecordInsights.Tests;

public class KustoCallRecordConversionTests
{
    private readonly JsonFlattener _flattener;
    private readonly string _minimalCallRecordJson;

    public KustoCallRecordConversionTests()
    {
        _flattener = new JsonFlattener(IKustoCallRecordHelpers.DefaultConfiguration);
        _minimalCallRecordJson = File.ReadAllText("TestData/minimal-callrecord.json");
    }

    #region Type Conversion Tests

    [Fact]
    public void FromJsonDictionary_GuidProperties_ParseCorrectly()
    {
        // Arrange & Act
        var results = _flattener.ProcessNode(_minimalCallRecordJson).ToList();
        var firstRow = results.First();

        // Extract as KustoCallRecord using reflection to access FromJsonDictionary
        var helperType = typeof(IKustoCallRecordHelpers);
        var method = helperType.GetMethod("FromJsonDictionary",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var kustoRecord = (IKustoCallRecord)method!.Invoke(null, new object[] { firstRow })!;

        // Assert - Test Guid parsing
        kustoRecord.CallId.Should().Be(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        kustoRecord.SessionId.Should().Be(Guid.Parse("00000000-0000-0000-0000-000000000100"));
        kustoRecord.Organizer_UserId.Should().Be(Guid.Parse("00000000-0000-0000-0000-000000000010"));
        kustoRecord.Organizer_UserTenantId.Should().Be(Guid.Parse("00000000-0000-0000-0000-000000000020"));
    }

    [Fact]
    public void FromJsonDictionary_DateTimeOffsetProperties_ParseCorrectly()
    {
        // Arrange & Act
        var results = _flattener.ProcessNode(_minimalCallRecordJson).ToList();
        var firstRow = results.First();

        var helperType = typeof(IKustoCallRecordHelpers);
        var method = helperType.GetMethod("FromJsonDictionary",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var kustoRecord = (IKustoCallRecord)method!.Invoke(null, new object[] { firstRow })!;

        // Assert - Test DateTimeOffset parsing
        kustoRecord.CallStartTime.Should().Be(DateTimeOffset.Parse("2026-01-13T09:00:00Z"));
        kustoRecord.CallEndTime.Should().Be(DateTimeOffset.Parse("2026-01-13T09:30:00Z"));
        kustoRecord.SessionStartTime.Should().Be(DateTimeOffset.Parse("2026-01-13T09:00:00Z"));
        kustoRecord.SessionEndTime.Should().Be(DateTimeOffset.Parse("2026-01-13T09:30:00Z"));
        kustoRecord.LastModifiedDateTimeOffset.Should().Be(DateTimeOffset.Parse("2026-01-13T10:00:00Z"));
    }

    [Fact]
    public void FromJsonDictionary_TimeSpanProperties_ParseCorrectly()
    {
        // Arrange & Act
        var results = _flattener.ProcessNode(_minimalCallRecordJson).ToList();
        var firstRow = results.First();

        var helperType = typeof(IKustoCallRecordHelpers);
        var method = helperType.GetMethod("FromJsonDictionary",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var kustoRecord = (IKustoCallRecord)method!.Invoke(null, new object[] { firstRow })!;

        // Assert - Test TimeSpan parsing from ISO 8601 duration format
        // PT0.02S = 0.02 seconds = 20 milliseconds
        kustoRecord.AverageAudioNetworkJitter.Should().Be(TimeSpan.FromMilliseconds(20));
        kustoRecord.MaxAudioNetworkJitter.Should().Be(TimeSpan.FromMilliseconds(50));
        kustoRecord.AverageJitter.Should().Be(TimeSpan.FromMilliseconds(20));
        kustoRecord.MaxJitter.Should().Be(TimeSpan.FromMilliseconds(50));
        kustoRecord.AverageRoundTripTime.Should().Be(TimeSpan.FromMilliseconds(50));
        kustoRecord.MaxRoundTripTime.Should().Be(TimeSpan.FromMilliseconds(100));
    }

    [Fact]
    public void FromJsonDictionary_StringProperties_ParseCorrectly()
    {
        // Arrange & Act
        var results = _flattener.ProcessNode(_minimalCallRecordJson).ToList();
        var firstRow = results.First();

        var helperType = typeof(IKustoCallRecordHelpers);
        var method = helperType.GetMethod("FromJsonDictionary",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var kustoRecord = (IKustoCallRecord)method!.Invoke(null, new object[] { firstRow })!;

        // Assert - Test string parsing
        kustoRecord.CallType.Should().Be("groupCall");
        kustoRecord.StreamId.Should().Be("1000");
        kustoRecord.StreamDirection.Should().Be("callerToCallee");
        kustoRecord.MediaLabel.Should().Be("main-audio");
        kustoRecord.Organizer_UserDisplayName.Should().Be("Test Organizer");
        kustoRecord.Caller_UserDisplayName.Should().Be("Test Caller");
        kustoRecord.Callee_UserDisplayName.Should().Be("Test Callee");
    }

    [Fact]
    public void FromJsonDictionary_LongProperties_ParseCorrectly()
    {
        // Arrange & Act
        var results = _flattener.ProcessNode(_minimalCallRecordJson).ToList();
        var firstRow = results.First();

        var helperType = typeof(IKustoCallRecordHelpers);
        var method = helperType.GetMethod("FromJsonDictionary",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var kustoRecord = (IKustoCallRecord)method!.Invoke(null, new object[] { firstRow })!;

        // Assert - Test long parsing
        kustoRecord.PacketUtilization.Should().Be(1000);
        kustoRecord.AverageBandwidthEstimate.Should().Be(50000);
    }

    [Fact]
    public void FromJsonDictionary_DoubleProperties_ParseCorrectly()
    {
        // Arrange & Act
        var results = _flattener.ProcessNode(_minimalCallRecordJson).ToList();
        var firstRow = results.First();

        var helperType = typeof(IKustoCallRecordHelpers);
        var method = helperType.GetMethod("FromJsonDictionary",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var kustoRecord = (IKustoCallRecord)method!.Invoke(null, new object[] { firstRow })!;

        // Assert - Test double parsing
        kustoRecord.AveragePacketLossRate.Should().Be(0.01);
        kustoRecord.MaxPacketLossRate.Should().Be(0.05);
    }

    [Fact]
    public void FromJsonDictionary_BoolProperties_ParseCorrectly()
    {
        // Arrange & Act
        var results = _flattener.ProcessNode(_minimalCallRecordJson).ToList();
        var firstRow = results.First();

        var helperType = typeof(IKustoCallRecordHelpers);
        var method = helperType.GetMethod("FromJsonDictionary",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var kustoRecord = (IKustoCallRecord)method!.Invoke(null, new object[] { firstRow })!;

        // Assert - Test bool parsing
        kustoRecord.WasMediaBypassed.Should().BeFalse();
    }

    [Fact]
    public void FromJsonDictionary_NullValues_HandleCorrectly()
    {
        // Arrange - Create JSON with null failureInfo fields
        var nullJson = @"{
            ""id"": ""00000000-0000-0000-0000-000000000001"",
            ""sessions"": [{
                ""id"": ""00000000-0000-0000-0000-000000000100"",
                ""failureInfo"": {
                    ""stage"": null,
                    ""reason"": null
                },
                ""segments"": [{
                    ""media"": [{
                        ""streams"": [{ ""streamId"": ""1000"" }]
                    }]
                }]
            }]
        }";

        // Act
        var results = _flattener.ProcessNode(nullJson).ToList();
        var firstRow = results.First();

        var helperType = typeof(IKustoCallRecordHelpers);
        var method = helperType.GetMethod("FromJsonDictionary",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var kustoRecord = (IKustoCallRecord)method!.Invoke(null, new object[] { firstRow })!;

        // Assert - Null values should be null
        kustoRecord.FailureStage.Should().BeNull();
        kustoRecord.FailureReason.Should().BeNull();
        kustoRecord.CallStartTime.Should().BeNull();
        kustoRecord.VideoCodec.Should().BeNull();
        kustoRecord.AudioCodec.Should().BeNull();
    }

    [Fact]
    public void FromJsonDictionary_MissingProperties_HandleCorrectly()
    {
        // Arrange - Create minimal JSON without optional fields
        var minimalJson = @"{
            ""id"": ""00000000-0000-0000-0000-000000000001"",
            ""sessions"": [{
                ""id"": ""00000000-0000-0000-0000-000000000100"",
                ""segments"": [{
                    ""media"": [{
                        ""streams"": [{ ""streamId"": ""1000"" }]
                    }]
                }]
            }]
        }";

        // Act
        var results = _flattener.ProcessNode(minimalJson).ToList();
        var firstRow = results.First();

        var helperType = typeof(IKustoCallRecordHelpers);
        var method = helperType.GetMethod("FromJsonDictionary",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var kustoRecord = (IKustoCallRecord)method!.Invoke(null, new object[] { firstRow })!;

        // Assert - Missing properties should be null
        kustoRecord.CallType.Should().BeNull();
        kustoRecord.CallStartTime.Should().BeNull();
        kustoRecord.CallEndTime.Should().BeNull();
        kustoRecord.VideoCodec.Should().BeNull();
        kustoRecord.AudioCodec.Should().BeNull();
        kustoRecord.WasMediaBypassed.Should().BeNull();
        kustoRecord.Organizer_UserDisplayName.Should().BeNull();
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void AsKustoCallRecords_WithMinimalCallRecord_ProducesValidRecords()
    {
        // This test validates the public API surface rather than internal methods
        // It ensures the complete flow from JSON to KustoCallRecord works

        // Arrange
        var callRecordJson = _minimalCallRecordJson;

        // Act
        var results = _flattener.ProcessNode(callRecordJson).ToList();

        // Assert
        results.Should().HaveCount(1);
        var firstRow = results.First();

        // Verify key fields are present in the flattened dictionary
        firstRow.Should().ContainKey(nameof(IKustoCallRecord.CallId));
        firstRow.Should().ContainKey(nameof(IKustoCallRecord.SessionId));
        firstRow.Should().ContainKey(nameof(IKustoCallRecord.StreamId));
        firstRow.Should().ContainKey(nameof(IKustoCallRecord.CallStartTime));
        firstRow.Should().ContainKey(nameof(IKustoCallRecord.CallEndTime));
        firstRow.Should().ContainKey(nameof(IKustoCallRecord.Organizer_UserDisplayName));
        firstRow.Should().ContainKey(nameof(IKustoCallRecord.Caller_UserDisplayName));
        firstRow.Should().ContainKey(nameof(IKustoCallRecord.Callee_UserDisplayName));
    }

    [Fact]
    public void AsKustoCallRecords_WithMultipleStreams_ProducesMultipleRecords()
    {
        // Arrange
        var multiStreamJson = @"{
            ""id"": ""call-123"",
            ""type"": ""groupCall"",
            ""startDateTime"": ""2026-01-13T09:00:00Z"",
            ""endDateTime"": ""2026-01-13T09:30:00Z"",
            ""sessions"": [{
                ""id"": ""session-1"",
                ""segments"": [{
                    ""media"": [{
                        ""label"": ""audio"",
                        ""streams"": [
                            {
                                ""streamId"": ""stream-1"",
                                ""streamDirection"": ""callerToCallee"",
                                ""wasMediaBypassed"": false,
                                ""averagePacketLossRate"": 0.01
                            },
                            {
                                ""streamId"": ""stream-2"",
                                ""streamDirection"": ""calleeToCaller"",
                                ""wasMediaBypassed"": false,
                                ""averagePacketLossRate"": 0.02
                            }
                        ]
                    }]
                }]
            }]
        }";

        // Act
        var results = _flattener.ProcessNode(multiStreamJson).ToList();

        // Assert
        results.Should().HaveCount(2, "2 streams should produce 2 flattened rows");

        // Verify both records have the correct stream IDs
        var streamIds = results
            .Select(r => r[nameof(IKustoCallRecord.StreamId)]?.AsValue().GetValue<string>())
            .ToList();
        streamIds.Should().Contain("stream-1");
        streamIds.Should().Contain("stream-2");

        // Verify both inherit the same CallId
        foreach (var row in results)
        {
            row[nameof(IKustoCallRecord.CallId)]?.AsValue().GetValue<string>()
                .Should().Be("call-123");
        }
    }

    [Fact]
    public void DefaultConfiguration_ContainsExpectedNumberOfColumns()
    {
        // Act
        var config = IKustoCallRecordHelpers.DefaultConfiguration;

        // Assert
        // The configuration should have 266 columns mapped
        config.Count().Should().BeGreaterThan(250,
            "DefaultConfiguration should contain approximately 266 property mappings");
    }

    [Fact]
    public void DefaultConfiguration_MapsKeyProperties()
    {
        // Act
        var config = IKustoCallRecordHelpers.DefaultConfiguration;
        var columnMap = new Dictionary<string, string>();
        foreach (var kvp in config)
        {
            columnMap[kvp.Key] = kvp.Value;
        }

        // Assert - Verify critical path mappings
        columnMap[nameof(IKustoCallRecord.CallId)].Should().Be("$.id");
        columnMap[nameof(IKustoCallRecord.SessionId)].Should().Be("$.sessions[*].id");
        columnMap[nameof(IKustoCallRecord.StreamId)].Should().Be("$.sessions[*].segments[*].media[*].streams[*].streamId");
        columnMap[nameof(IKustoCallRecord.CallType)].Should().Be("$.type");
        columnMap[nameof(IKustoCallRecord.CallStartTime)].Should().Be("$.startDateTime");
        columnMap[nameof(IKustoCallRecord.Organizer_UserDisplayName)].Should().Be("$.organizer.user.displayName");
        // Caller and Callee are at the segment level, not session level
        columnMap[nameof(IKustoCallRecord.Caller_UserDisplayName)].Should().Be("$.sessions[*].segments[*].caller.identity.user.displayName");
        columnMap[nameof(IKustoCallRecord.Callee_UserDisplayName)].Should().Be("$.sessions[*].segments[*].callee.identity.user.displayName");
    }

    #endregion
}
