using CallRecordInsights.Extensions;
using CallRecordInsights.Flattener;
using FluentAssertions;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.CallRecords;
using Microsoft.Kiota.Abstractions.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit.Abstractions;

using CallRecordDeviceInfo = Microsoft.Graph.Models.CallRecords.DeviceInfo;
using CallRecordMediaStream = Microsoft.Graph.Models.CallRecords.MediaStream;

namespace CallRecordInsights.Functions.Tests;

/// <summary>
/// End-to-end validation: builds a fully-populated CallRecord, serializes it via
/// the Graph SDK, runs it through the actual flattener, and verifies that every
/// configured JSON path produces a non-null value. Any null result indicates a
/// path mismatch (casing bug, renamed property, etc.).
/// </summary>
public class GraphSdkPathValidationTests
{
    private readonly ITestOutputHelper _output;

    public GraphSdkPathValidationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// Creates a CallRecord with every property populated that the flattener references.
    /// </summary>
    private static CallRecord BuildFullyPopulatedCallRecord()
    {
        var identity = new Identity
        {
            DisplayName = "Test User",
            Id = "user-id-001",
            AdditionalData = new Dictionary<string, object> { ["tenantId"] = "tenant-001" }
        };

        var communicationsIdentity = new CommunicationsIdentitySet
        {
            User = identity,
            ApplicationInstance = identity,
            Guest = identity,
            Phone = identity,
            OnPremises = identity,
            Encrypted = identity,
            AzureCommunicationServicesUser = identity,
            AdditionalData = new Dictionary<string, object>
            {
                // These are not typed in the SDK — they go through AdditionalData
                ["acsUser"] = CreateUntypedIdentity(),
                ["spoolUser"] = CreateUntypedIdentity(),
                ["acsApplicationInstance"] = CreateUntypedIdentity(),
                ["spoolApplicationInstance"] = CreateUntypedIdentity(),
            }
        };

        var networkInfo = new NetworkInfo
        {
            ConnectionType = NetworkConnectionType.Wired,
            ReflexiveIPAddress = "10.0.0.1",
            Subnet = "10.0.0.0/24",
            IpAddress = "10.0.0.1",
            MacAddress = "AA:BB:CC:DD:EE:FF",
            LinkSpeed = 1000000000,
            NetworkTransportProtocol = NetworkTransportProtocol.Udp,
            Port = 50000,
            RelayIPAddress = "10.0.0.2",
            RelayPort = 50001,
            DnsSuffix = "contoso.com",
            BasicServiceSetIdentifier = "bssid-001",
            WifiRadioType = WifiRadioType.Wifi80211ac,
            WifiBand = WifiBand.Frequency50GHz,
            WifiChannel = 36,
            WifiSignalStrength = -50,
            WifiBatteryCharge = 85,
            WifiMicrosoftDriver = "ms-driver",
            WifiMicrosoftDriverVersion = "1.0.0",
            WifiVendorDriver = "vendor-driver",
            WifiVendorDriverVersion = "2.0.0",
            SentQualityEventRatio = 0.01f,
            ReceivedQualityEventRatio = 0.02f,
            DelayEventRatio = 0.03f,
            BandwidthLowEventRatio = 0.04f,
            AdditionalData = new Dictionary<string, object>
            {
                ["traceRouteHops"] = "hop1,hop2"
            }
        };

        var deviceInfo = new CallRecordDeviceInfo
        {
            CaptureDeviceName = "Mic",
            CaptureDeviceDriver = "mic-driver",
            RenderDeviceName = "Speaker",
            RenderDeviceDriver = "speaker-driver",
            SentSignalLevel = -20,
            SentNoiseLevel = -50,
            MicGlitchRate = 0.01f,
            ReceivedSignalLevel = -22,
            ReceivedNoiseLevel = -52,
            SpeakerGlitchRate = 0.02f,
            HowlingEventCount = 0,
            InitialSignalLevelRootMeanSquare = 0.5f,
            DeviceGlitchEventRatio = 0.01f,
            DeviceClippingEventRatio = 0.02f,
            LowSpeechToNoiseEventRatio = 0.03f,
            CaptureNotFunctioningEventRatio = 0.0f,
            LowSpeechLevelEventRatio = 0.04f,
            RenderNotFunctioningEventRatio = 0.0f,
            RenderZeroVolumeEventRatio = 0.0f,
            RenderMuteEventRatio = 0.0f,
            CpuInsufficentEventRatio = 0.0f,
        };

        var mediaStream = new CallRecordMediaStream
        {
            StreamId = "stream-001",
            StreamDirection = MediaStreamDirection.CallerToCallee,
            VideoCodec = VideoCodec.H264,
            AudioCodec = AudioCodec.Opus,
            WasMediaBypassed = false,
            PacketUtilization = 1000L,
            AverageBandwidthEstimate = 500000L,
            AverageJitter = TimeSpan.FromMilliseconds(5),
            MaxJitter = TimeSpan.FromMilliseconds(15),
            AverageRoundTripTime = TimeSpan.FromMilliseconds(20),
            MaxRoundTripTime = TimeSpan.FromMilliseconds(50),
            AverageAudioNetworkJitter = TimeSpan.FromMilliseconds(3),
            MaxAudioNetworkJitter = TimeSpan.FromMilliseconds(10),
            AverageAudioDegradation = 0.1f,
            AveragePacketLossRate = 0.02f,
            MaxPacketLossRate = 0.05f,
            PostForwardErrorCorrectionPacketLossRate = 0.01f,
            AverageRatioOfConcealedSamples = 0.03f,
            MaxRatioOfConcealedSamples = 0.08f,
            LowVideoProcessingCapabilityRatio = 0.0f,
            AverageVideoFrameRate = 30.0f,
            AverageReceivedFrameRate = 29.5f,
            LowFrameRateRatio = 0.0f,
            AverageVideoPacketLossRate = 0.01f,
            AverageVideoFrameLossPercentage = 0.5f,
        };

        var userAgent = new ClientUserAgent
        {
            HeaderValue = "Teams/1.0",
            ProductFamily = ProductFamily.Teams,
            Platform = ClientPlatform.Windows,
            ApplicationVersion = "1.0.0",
            AzureADAppId = "app-id-001",
            CommunicationServiceId = "comms-001",
            AdditionalData = new Dictionary<string, object>
            {
                // "role" is not in the typed SDK model
                ["role"] = "test-role"
            }
        };

        var userFeedback = new UserFeedback
        {
            Rating = UserFeedbackRating.Good,
            Text = "Great call",
            AdditionalData = new Dictionary<string, object>
            {
                ["tokens"] = "token1"
            }
        };

        var participantEndpoint = new ParticipantEndpoint
        {
            Identity = communicationsIdentity,
            AssociatedIdentity = new Identity
            {
                Id = "associated-001",
                DisplayName = "Associated User",
                OdataType = "#microsoft.graph.identity",
                AdditionalData = new Dictionary<string, object>
                {
                    ["userPrincipalName"] = "user@contoso.com",
                    ["tenantId"] = "tenant-001",
                }
            },
            UserAgent = userAgent,
            Feedback = userFeedback,
        };

        return new CallRecord
        {
            Id = "call-001",
            StartDateTime = DateTimeOffset.UtcNow.AddMinutes(-30),
            EndDateTime = DateTimeOffset.UtcNow,
            LastModifiedDateTime = DateTimeOffset.UtcNow,
            Type = CallType.GroupCall,
            JoinWebUrl = "https://teams.microsoft.com/meet/test",
            Organizer = communicationsIdentity,
            AdditionalData = new Dictionary<string, object>
            {
                ["organizer_v2"] = new UntypedObject(new Dictionary<string, UntypedNode>
                {
                    ["id"] = new UntypedString("organizer-001"),
                    ["userPrincipalName"] = new UntypedString("organizer@contoso.com"),
                    ["displayName"] = new UntypedString("Organizer"),
                    ["tenantId"] = new UntypedString("tenant-001"),
                    ["@odata.type"] = new UntypedString("#microsoft.graph.callRecords.participantBase"),
                })
            },
            Sessions =
            [
                new Session
                {
                    Id = "session-001",
                    StartDateTime = DateTimeOffset.UtcNow.AddMinutes(-30),
                    EndDateTime = DateTimeOffset.UtcNow,
                    FailureInfo = new FailureInfo
                    {
                        Stage = FailureStage.CallSetup,
                        Reason = "test reason",
                    },
                    Segments =
                    [
                        new Segment
                        {
                            Callee = participantEndpoint,
                            Caller = participantEndpoint,
                            Media =
                            [
                                new Media
                                {
                                    Label = "main-audio",
                                    Streams = [mediaStream],
                                    CalleeNetwork = networkInfo,
                                    CallerNetwork = networkInfo,
                                    CalleeDevice = deviceInfo,
                                    CallerDevice = deviceInfo,
                                }
                            ]
                        }
                    ]
                }
            ],
        };
    }

    private static UntypedObject CreateUntypedIdentity()
    {
        return new UntypedObject(new Dictionary<string, UntypedNode>
        {
            ["displayName"] = new UntypedString("Test"),
            ["id"] = new UntypedString("id-001"),
            ["tenantId"] = new UntypedString("tenant-001")
        });
    }

    [Fact]
    public void AllFlattenerPaths_ProduceNonNullValues()
    {
        // Arrange
        var callRecord = BuildFullyPopulatedCallRecord();
        var json = callRecord.SerializeAsString();
        var config = IKustoCallRecordHelpers.DefaultConfiguration;
        var flattener = new JsonFlattener(config);

        // Known bug: azureAdAppId (flattener) vs azureADAppId (SDK) — casing mismatch.
        // See: GraphSerializationTests.cs for proof. Filed as a separate issue.
        var knownBugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Callee_AzureAdAppId",
            "Caller_AzureAdAppId",
        };

        // Act
        var results = flattener.ProcessNode(json);

        // Assert — we should get at least one flattened row
        results.Should().NotBeEmpty("a fully-populated CallRecord should produce at least one flattened row");
        var firstRow = results.First();

        var nullColumns = new List<string>();
        var knownBugColumns = new List<string>();
        var presentColumns = new List<string>();

        foreach (var columnName in config.Keys)
        {
            if (firstRow.TryGetValue(columnName, out var value) && value != null)
            {
                presentColumns.Add(columnName);
            }
            else if (knownBugs.Contains(columnName))
            {
                knownBugColumns.Add(columnName);
            }
            else
            {
                nullColumns.Add(columnName);
            }
        }

        _output.WriteLine($"Resolved: {presentColumns.Count}/{config.Count}");
        _output.WriteLine($"Known bugs (excluded): {knownBugColumns.Count}");

        if (knownBugColumns.Count > 0)
        {
            _output.WriteLine("\n=== KNOWN BUGS (excluded from assertion) ===");
            foreach (var col in knownBugColumns)
                _output.WriteLine($"  ⚠ {col} → {config[col]}");
        }

        if (nullColumns.Count > 0)
        {
            _output.WriteLine("\n=== NULL COLUMNS (path mismatch or missing data) ===");
            foreach (var col in nullColumns)
                _output.WriteLine($"  ✗ {col} → {config[col]}");
        }

        // Every non-known-bug path should resolve to a non-null value
        nullColumns.Should().BeEmpty(
            "all JSON paths should resolve when given a fully-populated CallRecord. " +
            "Null results indicate a path mismatch (e.g., casing bug) or a property " +
            "that the SDK doesn't serialize. Null columns: " +
            string.Join(", ", nullColumns));
    }
}
