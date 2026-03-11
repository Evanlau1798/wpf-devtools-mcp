using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.Tools;
using Xunit;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class TreeRequestOptionsErrorContractTests
{
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
}
