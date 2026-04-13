using FluentAssertions;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Http.HttpClientLibrary.Middleware.Options;
using System.Collections.Generic;
using System.Linq;

namespace CallRecordInsights.Functions.Tests;

public class GraphRequestBuilderAppOnlyExtensionsTests
{
    [Fact]
    public void WithUserAgent_SetsProductName()
    {
        // Arrange
        var options = new List<IRequestOption>();

        // Act
        options.WithUserAgent();

        // Assert
        var uaOption = options.OfType<UserAgentHandlerOption>().Single();
        uaOption.ProductName.Should().Be("CallRecordInsights");
    }

    [Fact]
    public void WithUserAgent_SetsVersionFromAssembly()
    {
        // Arrange
        var options = new List<IRequestOption>();

        // Act
        options.WithUserAgent();

        // Assert
        var uaOption = options.OfType<UserAgentHandlerOption>().Single();
        uaOption.ProductVersion.Should().NotBeNullOrEmpty("version should be read from assembly");
        uaOption.ProductVersion.Should().MatchRegex(@"^\d+\.\d+\.\d+",
            "version should be a valid semver format");
    }

    [Fact]
    public void WithUserAgent_EnablesUserAgentHeader()
    {
        // Arrange
        var options = new List<IRequestOption>();

        // Act
        options.WithUserAgent();

        // Assert
        var uaOption = options.OfType<UserAgentHandlerOption>().Single();
        uaOption.Enabled.Should().BeTrue();
    }

    [Fact]
    public void WithUserAgent_ReusesExistingOption()
    {
        // Arrange
        var options = new List<IRequestOption>();
        var existingOption = new UserAgentHandlerOption();
        options.Add(existingOption);

        // Act
        options.WithUserAgent();

        // Assert
        options.OfType<UserAgentHandlerOption>().Should().HaveCount(1,
            "should reuse existing option, not add a new one");
    }
}
