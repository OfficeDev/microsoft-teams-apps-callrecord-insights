using FluentAssertions;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.CallRecords;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit.Abstractions;

using CallRecordDeviceInfo = Microsoft.Graph.Models.CallRecords.DeviceInfo;
using CallRecordMediaStream = Microsoft.Graph.Models.CallRecords.MediaStream;

namespace CallRecordInsights.Functions.Tests;

/// <summary>
/// Discovers all properties on Graph SDK types used by the flattener and compares
/// against a known baseline. New properties are reported as test output and cause
/// a test failure to surface them during Graph SDK dependabot updates.
/// </summary>
public class GraphSdkPropertyDiscoveryTests
{
    private readonly ITestOutputHelper _output;

    public GraphSdkPropertyDiscoveryTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// All Graph SDK types whose properties the flattener may consume.
    /// </summary>
    private static readonly Dictionary<string, Type> TrackedTypes = new()
    {
        ["CallRecord"] = typeof(CallRecord),
        ["Session"] = typeof(Session),
        ["Segment"] = typeof(Segment),
        ["ParticipantEndpoint"] = typeof(ParticipantEndpoint),
        ["UserAgent"] = typeof(UserAgent),
        ["ClientUserAgent"] = typeof(ClientUserAgent),
        ["ServiceUserAgent"] = typeof(ServiceUserAgent),
        ["UserFeedback"] = typeof(UserFeedback),
        ["FailureInfo"] = typeof(FailureInfo),
        ["Media"] = typeof(Media),
        ["MediaStream"] = typeof(CallRecordMediaStream),
        ["NetworkInfo"] = typeof(NetworkInfo),
        ["DeviceInfo"] = typeof(CallRecordDeviceInfo),
        ["CommunicationsIdentitySet"] = typeof(CommunicationsIdentitySet),
        ["Identity"] = typeof(Identity),
    };

    private static Dictionary<string, List<string>> GetCurrentProperties()
    {
        var result = new Dictionary<string, List<string>>();
        foreach (var (name, type) in TrackedTypes)
        {
            var instance = (Microsoft.Kiota.Abstractions.Serialization.IParsable)Activator.CreateInstance(type)!;
            var keys = instance.GetFieldDeserializers().Keys
                .Where(k => k != "@odata.type")
                .OrderBy(k => k)
                .ToList();
            result[name] = keys;
        }
        return result;
    }

    private static string GetBaselinePath()
    {
        // Walk up from bin/{Configuration}/{TFM} to the test project root
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "CallRecordInsights.Functions.Tests.csproj")))
            dir = Directory.GetParent(dir)?.FullName;
        return Path.Combine(dir ?? AppContext.BaseDirectory, "GraphSdkPropertyBaseline.json");
    }

    [Fact]
    [Trait("Category", "Manual")]
    public void GenerateBaseline_IfMissing()
    {
        const string generateBaselineEnvironmentVariable = "GENERATE_GRAPH_SDK_PROPERTY_BASELINE";
        var shouldGenerateBaseline = string.Equals(
            Environment.GetEnvironmentVariable(generateBaselineEnvironmentVariable),
            "true",
            StringComparison.OrdinalIgnoreCase);

        if (!shouldGenerateBaseline)
        {
            _output.WriteLine(
                $"Skipping baseline generation. Set {generateBaselineEnvironmentVariable}=true to generate or regenerate GraphSdkPropertyBaseline.json.");
            return;
        }

        var baselinePath = GetBaselinePath();
        if (File.Exists(baselinePath))
        {
            _output.WriteLine($"Baseline already exists at {baselinePath}. Delete it to regenerate.");
            return;
        }

        var current = GetCurrentProperties();
        var json = JsonSerializer.Serialize(current, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(baselinePath, json);
        _output.WriteLine($"Baseline generated at {baselinePath}");
    }

    [Fact]
    public void DetectNewGraphProperties()
    {
        var baselinePath = GetBaselinePath();
        if (!File.Exists(baselinePath))
        {
            Assert.Fail(
                $"Required Graph SDK property baseline file is missing: {baselinePath}. " +
                $"Regenerate it by running {nameof(GenerateBaseline_IfMissing)} locally with GENERATE_GRAPH_SDK_PROPERTY_BASELINE=true and commit the resulting baseline file.");
        }

        var baselineJson = File.ReadAllText(baselinePath);
        var baseline = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(baselineJson)!;
        var current = GetCurrentProperties();

        var newProperties = new Dictionary<string, List<string>>();

        foreach (var (typeName, currentProps) in current)
        {
            if (!baseline.TryGetValue(typeName, out var baselineProps))
            {
                newProperties[typeName] = currentProps;
                continue;
            }

            var additions = currentProps.Except(baselineProps).ToList();
            if (additions.Count > 0)
                newProperties[typeName] = additions;
        }

        if (newProperties.Count > 0)
        {
            _output.WriteLine("=== NEW GRAPH SDK PROPERTIES DETECTED ===");
            foreach (var (typeName, props) in newProperties)
            {
                foreach (var prop in props)
                    _output.WriteLine($"  {typeName}.{prop}");
            }
            _output.WriteLine("==========================================");
            _output.WriteLine("Consider adding these to the flattener configuration.");
            _output.WriteLine($"Update the baseline: delete {baselinePath} and re-run tests.");

            Assert.Fail(
                $"New Graph SDK properties detected on tracked types: " +
                string.Join(", ", newProperties.SelectMany(kvp => kvp.Value.Select(p => $"{kvp.Key}.{p}"))));
        }
        else
        {
            _output.WriteLine("No new properties detected. Baseline is current.");
        }
    }
}
