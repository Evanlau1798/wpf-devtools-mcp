using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.Tools;
using Xunit;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class TreeRequestOptionsErrorContractTests
{
    private const int ExpectedDefaultMaxNodes = 1000;
    private const int ExpectedDefaultMaxChildrenPerNode = 200;

    [Fact]
    public void TryParse_InvalidDepth_ShouldReturnStructuredInvalidArgument()
    {
        var arguments = JsonSerializer.SerializeToElement(new { depth = 101 });

        var success = TreeRequestOptions.TryParse(arguments, out _, out var error);

        success.Should().BeFalse();
        error.Should().NotBeNull();
        var json = JsonSerializer.SerializeToElement(error);
        json.GetProperty("success").GetBoolean().Should().BeFalse();
        json.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
        json.GetProperty("hint").GetString().Should().Contain("depth");
    }

    [Fact]
    public void TryParse_InvalidMaxNodes_ShouldReturnStructuredInvalidArgument()
    {
        var arguments = JsonSerializer.SerializeToElement(new { maxNodes = 0 });

        var success = TreeRequestOptions.TryParse(arguments, out _, out var error);

        success.Should().BeFalse();
        error.Should().NotBeNull();
        var json = JsonSerializer.SerializeToElement(error);
        json.GetProperty("success").GetBoolean().Should().BeFalse();
        json.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
        json.GetProperty("hint").GetString().Should().Contain("maxNodes");
    }

    [Fact]
    public void TryParse_WhenCapsAreOmitted_ShouldApplySafeDefaultCaps()
    {
        var arguments = JsonSerializer.SerializeToElement(new { });

        var success = TreeRequestOptions.TryParse(arguments, out var options, out var error);

        success.Should().BeTrue();
        error.Should().BeNull();
        options.MaxNodes.Should().Be(ExpectedDefaultMaxNodes);
        options.MaxChildrenPerNode.Should().Be(ExpectedDefaultMaxChildrenPerNode);
    }
}
