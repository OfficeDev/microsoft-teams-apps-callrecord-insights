using Azure.Core;
using Azure.Storage.Queues;
using CallRecordInsights.Flattener;
using CallRecordInsights.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using System;
using System.Collections.Generic;
using Xunit;

namespace CallRecordInsights.Functions.Tests;

/// <summary>
/// Tests to verify DI container configuration and service resolution.
/// Critical for catching breaking changes in NuGet packages that affect service registration.
/// </summary>
public class DependencyInjectionTests : IDisposable
{
    private readonly IHost _testHost;
    private readonly IServiceProvider _services;

    public DependencyInjectionTests()
    {
        // Build a test configuration with minimal required settings
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["AzureAd:TenantId"] = "00000000-0000-0000-0000-000000000001",
                ["AzureAd:ClientId"] = "00000000-0000-0000-0000-000000000002",
                ["GraphSubscription:Endpoint"] = "graph.microsoft.com",
                ["GraphSubscription:NotificationUrl"] = "https://example.com/notifications",
                ["GraphSubscription:Resource"] = "/communications/callRecords",
                ["GraphSubscription:ChangeType"] = "created",
                ["GraphSubscription:ClientState"] = "test-client-state",
                ["GraphSubscription:Tenants"] = "00000000-0000-0000-0000-000000000001",
                ["CallRecordInsightsDb:EndpointUri"] = "https://localhost:8081",
                ["CallRecordInsightsDb:Database"] = "TestDb",
                ["CallRecordInsightsDb:ProcessedContainer"] = "TestContainer",
                ["CallRecordsQueueConnection"] = "UseDevelopmentStorage=true",
            })
            .Build();

        // Build test host with same DI registration as actual Program.cs
        _testHost = new HostBuilder()
            .ConfigureAppConfiguration(builder => builder.AddConfiguration(configuration))
            .ConfigureServices((context, services) =>
            {
                // Register services exactly as Program.cs does
                services.AddSingleton<IConfiguration>(configuration);

                // Add the same service registrations as the real application
                services.AddJsonToKustoFlattener();
                services.AddCallRecordsGraphContext();
                services.AddCallRecordsDataContext();
                services.AddCallRecordsQueueContext();
            })
            .Build();

        _services = _testHost.Services;
    }

    [Fact]
    public void ServiceProvider_CanResolveJsonProcessor()
    {
        // Act
        var service = _services.GetService<IJsonProcessor>();

        // Assert
        service.Should().NotBeNull("JsonProcessor should be registered");
        service.Should().BeOfType<JsonFlattener>("Should resolve to concrete type");
    }

    [Fact]
    public void ServiceProvider_CanResolveJsonFlattenerConfiguration()
    {
        // Act
        var config = _services.GetService<IJsonFlattenerConfiguration>();

        // Assert
        config.Should().NotBeNull("JsonFlattenerConfiguration should be registered");
        config.Count().Should().BeGreaterThan(250, "Should have ~266 property mappings");
    }

    [Fact]
    public void ServiceProvider_CanResolveCallRecordsGraphContext()
    {
        // Act
        var service = _services.GetService<ICallRecordsGraphContext>();

        // Assert
        service.Should().NotBeNull("CallRecordsGraphContext should be registered");
        service.Should().BeAssignableTo<ICallRecordsGraphContext>();
    }

    [Fact]
    public void ServiceProvider_CanResolveCallRecordsDataContext()
    {
        // Act
        var service = _services.GetService<ICallRecordsDataContext>();

        // Assert
        service.Should().NotBeNull("CallRecordsDataContext should be registered");
        service.Should().BeAssignableTo<ICallRecordsDataContext>();
    }

    [Fact]
    public void ServiceProvider_CanResolveQueueServiceClient()
    {
        // Act
        var service = _services.GetService<QueueServiceClient>();

        // Assert
        service.Should().NotBeNull("QueueServiceClient should be registered");
    }

    [Fact]
    public void ServiceProvider_CanResolveGraphServiceClient()
    {
        // Act
        var service = _services.GetService<GraphServiceClient>();

        // Assert
        service.Should().NotBeNull("GraphServiceClient should be registered");
        service.Should().BeOfType<GraphServiceClient>();
    }

    [Fact]
    public void ServiceProvider_CanResolveTokenCredential()
    {
        // Act
        var credential = _services.GetService<TokenCredential>();

        // Assert
        credential.Should().NotBeNull("TokenCredential should be registered");
        credential.Should().BeAssignableTo<TokenCredential>("Should resolve to ChainedTokenCredential or compatible type");
    }

    [Fact]
    public void ServiceProvider_CanResolveLogger()
    {
        // Act
        var logger = _services.GetService<ILogger<DependencyInjectionTests>>();

        // Assert
        logger.Should().NotBeNull("Logger should be available from DI");
    }

    [Fact]
    public void ServiceProvider_CanResolveConfiguration()
    {
        // Act
        var config = _services.GetService<IConfiguration>();

        // Assert
        config.Should().NotBeNull("IConfiguration should be registered");
        config["AzureAd:TenantId"].Should().Be("00000000-0000-0000-0000-000000000001");
    }

    [Fact]
    public void GraphServiceClient_HasCorrectBaseUrl()
    {
        // Arrange
        var graphClient = _services.GetRequiredService<GraphServiceClient>();

        // Act
        var requestAdapter = graphClient.RequestAdapter;

        // Assert
        requestAdapter.Should().NotBeNull("RequestAdapter should be configured");
        requestAdapter.BaseUrl.Should().Contain("graph.microsoft.com", "Should use configured endpoint");
    }

    [Fact]
    public void CallRecordsGraphContext_ResolvesWithoutErrors()
    {
        // This test verifies that CallRecordsGraphContext can be resolved from DI
        // without throwing exceptions during construction.
        // Note: We don't access the Tenants property as it triggers HTTP calls
        // to validate tenant GUIDs, which would fail in a unit test environment.

        // Act
        var context = _services.GetService<ICallRecordsGraphContext>();

        // Assert
        context.Should().NotBeNull("CallRecordsGraphContext should resolve from DI");
    }

    [Fact]
    public void AllCriticalServices_CanBeResolved()
    {
        // This test verifies that all services work together without circular dependencies
        // or missing registrations

        // Act & Assert - if any service fails to resolve, this will throw
        var services = new Dictionary<string, object>
        {
            ["JsonProcessor"] = _services.GetRequiredService<IJsonProcessor>(),
            ["JsonFlattenerConfiguration"] = _services.GetRequiredService<IJsonFlattenerConfiguration>(),
            ["CallRecordsGraphContext"] = _services.GetRequiredService<ICallRecordsGraphContext>(),
            ["CallRecordsDataContext"] = _services.GetRequiredService<ICallRecordsDataContext>(),
            ["QueueServiceClient"] = _services.GetRequiredService<QueueServiceClient>(),
            ["GraphServiceClient"] = _services.GetRequiredService<GraphServiceClient>(),
            ["TokenCredential"] = _services.GetRequiredService<TokenCredential>(),
            ["Configuration"] = _services.GetRequiredService<IConfiguration>(),
        };

        // Verify all resolved successfully
        foreach (var kvp in services)
        {
            kvp.Value.Should().NotBeNull($"{kvp.Key} should resolve from DI container");
        }
    }

    public void Dispose()
    {
        _testHost?.Dispose();
    }
}
