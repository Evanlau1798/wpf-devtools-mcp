using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.McpResources;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class ResponseContractResourceOperationalGuidanceTests
{
    [Fact]
    public void ResponseContractResource_ShouldExposeMillisecondRateLimitBackoff()
    {
        using var document = JsonDocument.Parse(CapabilityResources.GetResponseContract());
        var errorPayload = document.RootElement.GetProperty("errorPayload");

        errorPayload.GetProperty("compatibilityProjectionFields")
            .EnumerateArray()
            .Select(field => field.GetString())
            .Should()
            .Contain("retryAfterMs");

        var retryAfterMs = errorPayload
            .GetProperty("recovery")
            .GetProperty("properties")
            .GetProperty("retryAfterMs");
        retryAfterMs.GetProperty("type").GetString().Should().Be("integer");
        retryAfterMs.GetProperty("optional").GetBoolean().Should().BeTrue();
    }
}
