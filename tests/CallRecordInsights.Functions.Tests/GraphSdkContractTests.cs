using FluentAssertions;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.CallRecords;
using System.Collections.Generic;
using System.Linq;

// Disambiguate types that exist in both Microsoft.Graph.Models and Microsoft.Graph.Models.CallRecords
using CallRecordEndpoint = Microsoft.Graph.Models.CallRecords.Endpoint;
using CallRecordMediaStream = Microsoft.Graph.Models.CallRecords.MediaStream;
using CallRecordDeviceInfo = Microsoft.Graph.Models.CallRecords.DeviceInfo;

namespace CallRecordInsights.Functions.Tests;

/// <summary>
/// Verifies that the Microsoft Graph SDK types still expose the JSON properties
/// referenced by the flattener's JSON path mappings. If a Graph SDK update removes
/// or renames a property, these tests fail — blocking auto-merge of that update.
/// </summary>
public class GraphSdkContractTests
{
    private static IDictionary<string, Action<Microsoft.Kiota.Abstractions.Serialization.IParseNode>>
        GetDeserializers<T>() where T : Microsoft.Kiota.Abstractions.Serialization.IParsable, new()
        => new T().GetFieldDeserializers();

    // ──────────────────────────────────────────────
    // CallRecord (root type)
    // ──────────────────────────────────────────────

    public static IEnumerable<object[]> CallRecordProperties =>
    [
        ["id"],
        ["startDateTime"],
        ["endDateTime"],
        ["lastModifiedDateTime"],
        ["type"],
        ["joinWebUrl"],
        ["sessions"],
        ["organizer"],
    ];

    [Theory]
    [MemberData(nameof(CallRecordProperties))]
    public void CallRecord_HasRequiredProperty(string jsonPropertyName)
    {
        var deserializers = GetDeserializers<CallRecord>();
        deserializers.Keys.Should().Contain(jsonPropertyName,
            $"CallRecord must expose '{jsonPropertyName}' for the flattener JSON paths to resolve");
    }

    // ──────────────────────────────────────────────
    // Session
    // ──────────────────────────────────────────────

    public static IEnumerable<object[]> SessionProperties =>
    [
        ["id"],
        ["startDateTime"],
        ["endDateTime"],
        ["failureInfo"],
        ["segments"],
    ];

    [Theory]
    [MemberData(nameof(SessionProperties))]
    public void Session_HasRequiredProperty(string jsonPropertyName)
    {
        var deserializers = GetDeserializers<Session>();
        deserializers.Keys.Should().Contain(jsonPropertyName,
            $"Session must expose '{jsonPropertyName}'");
    }

    // ──────────────────────────────────────────────
    // FailureInfo
    // ──────────────────────────────────────────────

    public static IEnumerable<object[]> FailureInfoProperties =>
    [
        ["stage"],
        ["reason"],
    ];

    [Theory]
    [MemberData(nameof(FailureInfoProperties))]
    public void FailureInfo_HasRequiredProperty(string jsonPropertyName)
    {
        var deserializers = GetDeserializers<FailureInfo>();
        deserializers.Keys.Should().Contain(jsonPropertyName,
            $"FailureInfo must expose '{jsonPropertyName}'");
    }

    // ──────────────────────────────────────────────
    // Segment
    // ──────────────────────────────────────────────

    public static IEnumerable<object[]> SegmentProperties =>
    [
        ["callee"],
        ["caller"],
        ["media"],
    ];

    [Theory]
    [MemberData(nameof(SegmentProperties))]
    public void Segment_HasRequiredProperty(string jsonPropertyName)
    {
        var deserializers = GetDeserializers<Segment>();
        deserializers.Keys.Should().Contain(jsonPropertyName,
            $"Segment must expose '{jsonPropertyName}'");
    }

    // ──────────────────────────────────────────────
    // ParticipantEndpoint (callee/caller endpoint type)
    // ──────────────────────────────────────────────

    public static IEnumerable<object[]> ParticipantEndpointProperties =>
    [
        ["identity"],
        ["associatedIdentity"],
        ["userAgent"],
        ["feedback"],
    ];

    [Theory]
    [MemberData(nameof(ParticipantEndpointProperties))]
    public void ParticipantEndpoint_HasRequiredProperty(string jsonPropertyName)
    {
        var deserializers = GetDeserializers<ParticipantEndpoint>();
        deserializers.Keys.Should().Contain(jsonPropertyName,
            $"ParticipantEndpoint must expose '{jsonPropertyName}'");
    }

    // ──────────────────────────────────────────────
    // UserAgent (base agent properties)
    // ──────────────────────────────────────────────

    public static IEnumerable<object[]> UserAgentProperties =>
    [
        ["headerValue"],
    ];

    [Theory]
    [MemberData(nameof(UserAgentProperties))]
    public void UserAgent_HasRequiredProperty(string jsonPropertyName)
    {
        var deserializers = GetDeserializers<UserAgent>();
        deserializers.Keys.Should().Contain(jsonPropertyName,
            $"UserAgent must expose '{jsonPropertyName}'");
    }

    // ──────────────────────────────────────────────
    // ClientUserAgent (extends UserAgent with app details)
    // ──────────────────────────────────────────────

    public static IEnumerable<object[]> ClientUserAgentProperties =>
    [
        ["productFamily"],
        ["platform"],
        ["applicationVersion"],
        ["azureADAppId"],
        ["communicationServiceId"],
    ];

    /// <remarks>
    /// The JSON path "callee.userAgent.role" uses a property name that is not in the
    /// SDK's typed model (it flows through AdditionalData). It cannot break from SDK
    /// updates but also cannot be contract-tested here.
    /// </remarks>

    [Theory]
    [MemberData(nameof(ClientUserAgentProperties))]
    public void ClientUserAgent_HasRequiredProperty(string jsonPropertyName)
    {
        var deserializers = GetDeserializers<ClientUserAgent>();
        deserializers.Keys.Should().Contain(jsonPropertyName,
            $"ClientUserAgent must expose '{jsonPropertyName}'");
    }

    // ──────────────────────────────────────────────
    // UserFeedback
    // ──────────────────────────────────────────────

    public static IEnumerable<object[]> UserFeedbackProperties =>
    [
        ["rating"],
        ["text"],
        ["tokens"],
    ];

    [Theory]
    [MemberData(nameof(UserFeedbackProperties))]
    public void UserFeedback_HasRequiredProperty(string jsonPropertyName)
    {
        var deserializers = GetDeserializers<UserFeedback>();
        deserializers.Keys.Should().Contain(jsonPropertyName,
            $"UserFeedback must expose '{jsonPropertyName}'");
    }

    // ──────────────────────────────────────────────
    // Media
    // ──────────────────────────────────────────────

    public static IEnumerable<object[]> MediaProperties =>
    [
        ["label"],
        ["streams"],
        ["calleeNetwork"],
        ["callerNetwork"],
        ["calleeDevice"],
        ["callerDevice"],
    ];

    [Theory]
    [MemberData(nameof(MediaProperties))]
    public void Media_HasRequiredProperty(string jsonPropertyName)
    {
        var deserializers = GetDeserializers<Media>();
        deserializers.Keys.Should().Contain(jsonPropertyName,
            $"Media must expose '{jsonPropertyName}'");
    }

    // ──────────────────────────────────────────────
    // MediaStream (QoS metrics)
    // ──────────────────────────────────────────────

    public static IEnumerable<object[]> MediaStreamProperties =>
    [
        ["streamId"],
        ["streamDirection"],
        ["videoCodec"],
        ["audioCodec"],
        ["wasMediaBypassed"],
        ["packetUtilization"],
        ["averageBandwidthEstimate"],
        ["averageJitter"],
        ["maxJitter"],
        ["averageRoundTripTime"],
        ["maxRoundTripTime"],
        ["averageAudioNetworkJitter"],
        ["maxAudioNetworkJitter"],
        ["averageAudioDegradation"],
        ["averagePacketLossRate"],
        ["maxPacketLossRate"],
        ["postForwardErrorCorrectionPacketLossRate"],
        ["averageRatioOfConcealedSamples"],
        ["maxRatioOfConcealedSamples"],
        ["lowVideoProcessingCapabilityRatio"],
        ["averageVideoFrameRate"],
        ["averageReceivedFrameRate"],
        ["lowFrameRateRatio"],
        ["averageVideoPacketLossRate"],
        ["averageVideoFrameLossPercentage"],
    ];

    [Theory]
    [MemberData(nameof(MediaStreamProperties))]
    public void MediaStream_HasRequiredProperty(string jsonPropertyName)
    {
        var deserializers = GetDeserializers<CallRecordMediaStream>();
        deserializers.Keys.Should().Contain(jsonPropertyName,
            $"MediaStream must expose '{jsonPropertyName}'");
    }

    // ──────────────────────────────────────────────
    // NetworkInfo (callee/caller network)
    // ──────────────────────────────────────────────

    public static IEnumerable<object[]> NetworkInfoProperties =>
    [
        ["connectionType"],
        ["reflexiveIPAddress"],
        ["subnet"],
        ["ipAddress"],
        ["macAddress"],
        ["linkSpeed"],
        ["networkTransportProtocol"],
        ["port"],
        ["relayIPAddress"],
        ["relayPort"],
        ["dnsSuffix"],
        ["traceRouteHops"],
        ["basicServiceSetIdentifier"],
        ["wifiRadioType"],
        ["wifiBand"],
        ["wifiChannel"],
        ["wifiSignalStrength"],
        ["wifiBatteryCharge"],
        ["wifiMicrosoftDriver"],
        ["wifiMicrosoftDriverVersion"],
        ["wifiVendorDriver"],
        ["wifiVendorDriverVersion"],
        ["sentQualityEventRatio"],
        ["receivedQualityEventRatio"],
        ["delayEventRatio"],
        ["bandwidthLowEventRatio"],
    ];

    [Theory]
    [MemberData(nameof(NetworkInfoProperties))]
    public void NetworkInfo_HasRequiredProperty(string jsonPropertyName)
    {
        var deserializers = GetDeserializers<NetworkInfo>();
        deserializers.Keys.Should().Contain(jsonPropertyName,
            $"NetworkInfo must expose '{jsonPropertyName}'");
    }

    // ──────────────────────────────────────────────
    // DeviceInfo (callee/caller device)
    // ──────────────────────────────────────────────

    public static IEnumerable<object[]> DeviceInfoProperties =>
    [
        ["captureDeviceName"],
        ["captureDeviceDriver"],
        ["renderDeviceName"],
        ["renderDeviceDriver"],
        ["sentSignalLevel"],
        ["sentNoiseLevel"],
        ["micGlitchRate"],
        ["receivedSignalLevel"],
        ["receivedNoiseLevel"],
        ["speakerGlitchRate"],
        ["howlingEventCount"],
        ["initialSignalLevelRootMeanSquare"],
        ["deviceGlitchEventRatio"],
        ["deviceClippingEventRatio"],
        ["lowSpeechToNoiseEventRatio"],
        ["captureNotFunctioningEventRatio"],
        ["lowSpeechLevelEventRatio"],
        ["renderNotFunctioningEventRatio"],
        ["renderZeroVolumeEventRatio"],
        ["renderMuteEventRatio"],
        ["cpuInsufficentEventRatio"],
    ];

    [Theory]
    [MemberData(nameof(DeviceInfoProperties))]
    public void DeviceInfo_HasRequiredProperty(string jsonPropertyName)
    {
        var deserializers = GetDeserializers<CallRecordDeviceInfo>();
        deserializers.Keys.Should().Contain(jsonPropertyName,
            $"DeviceInfo must expose '{jsonPropertyName}'");
    }

    // ──────────────────────────────────────────────
    // CommunicationsIdentitySet (organizer/callee/caller identity)
    // ──────────────────────────────────────────────

    public static IEnumerable<object[]> CommunicationsIdentitySetProperties =>
    [
        ["user"],
        ["applicationInstance"],
        ["guest"],
        ["phone"],
        ["onPremises"],
        ["encrypted"],
        ["azureCommunicationServicesUser"],
    ];

    /// <remarks>
    /// The flattener JSON paths reference abbreviated identity names (acsUser, spoolUser,
    /// acsApplicationInstance, spoolApplicationInstance) that are not in the SDK's typed
    /// model — they flow through AdditionalData. The SDK uses the full name
    /// "azureCommunicationServicesUser" for what the API returns as "acsUser".
    /// The spool* types are entirely untyped in the SDK.
    /// </remarks>

    [Theory]
    [MemberData(nameof(CommunicationsIdentitySetProperties))]
    public void CommunicationsIdentitySet_HasRequiredProperty(string jsonPropertyName)
    {
        var deserializers = GetDeserializers<CommunicationsIdentitySet>();
        deserializers.Keys.Should().Contain(jsonPropertyName,
            $"CommunicationsIdentitySet must expose '{jsonPropertyName}'");
    }

    // ──────────────────────────────────────────────
    // Identity (leaf identity fields: displayName, id, tenantId)
    // ──────────────────────────────────────────────

    public static IEnumerable<object[]> IdentityProperties =>
    [
        ["displayName"],
        ["id"],
    ];

    [Theory]
    [MemberData(nameof(IdentityProperties))]
    public void Identity_HasRequiredProperty(string jsonPropertyName)
    {
        var deserializers = GetDeserializers<Identity>();
        deserializers.Keys.Should().Contain(jsonPropertyName,
            $"Identity must expose '{jsonPropertyName}'");
    }

    // ──────────────────────────────────────────────
    // Type hierarchy checks
    // ──────────────────────────────────────────────

    [Fact]
    public void ParticipantEndpoint_InheritsFromEndpoint()
    {
        typeof(ParticipantEndpoint).Should().BeAssignableTo<CallRecordEndpoint>(
            "Segment.Callee/Caller deserializes as ParticipantEndpoint, which must be an Endpoint subtype");
    }

    [Fact]
    public void ClientUserAgent_InheritsFromUserAgent()
    {
        typeof(ClientUserAgent).Should().BeAssignableTo<UserAgent>(
            "UserAgent properties used in JSON paths span both UserAgent and ClientUserAgent");
    }

    [Fact]
    public void CommunicationsIdentitySet_InheritsFromIdentitySet()
    {
        typeof(CommunicationsIdentitySet).Should().BeAssignableTo<IdentitySet>(
            "Identity resolution uses CommunicationsIdentitySet which extends IdentitySet");
    }
}
