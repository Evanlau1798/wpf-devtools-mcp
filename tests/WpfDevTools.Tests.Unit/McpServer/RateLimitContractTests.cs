using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer;

/// <summary>
/// Contract tests ensuring rate-limit error responses are consistent
/// across all tool paths and match ServerInstructions documentation.
/// </summary>
public class RateLimitContractTests
{
    [Fact]
    public async Task ConnectTool_RateLimitResponse_ShouldContainRetryAfterSeconds()
    {
        // Arrange
        using var _ = new SkipSignatureCheckScope();
        var sessionManager = new SessionManager(maxRequestsPerMinute: 1);
        var tool = new ConnectTool(sessionManager);
        var parameters = new { processId = 12345 };

        // Consume rate limit
        await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        // Act - second request should be rate limited
        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        // Assert - must contain numeric retryAfterSeconds
        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("success").GetBoolean().Should().BeFalse();
        json.TryGetProperty("retryAfterSeconds", out var retryProp).Should().BeTrue(
            "rate limit response must include retryAfterSeconds for machine-readable retry logic");
        retryProp.GetInt32().Should().Be(60);
    }

    [Fact]
    public async Task ConnectTool_RateLimitResponse_ShouldContainHumanReadableRetryAfter()
    {
        // Arrange
        using var _ = new SkipSignatureCheckScope();
        var sessionManager = new SessionManager(maxRequestsPerMinute: 1);
        var tool = new ConnectTool(sessionManager);
        var parameters = new { processId = 12345 };

        // Consume rate limit
        await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        // Act
        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        // Assert - must contain human-readable retryAfter string
        var json = JsonSerializer.SerializeToElement(result);
        json.TryGetProperty("retryAfter", out var retryProp).Should().BeTrue(
            "rate limit response must include retryAfter for human-readable guidance");
        retryProp.GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ConnectTool_RateLimitResponse_ShouldContainAvailableTokens()
    {
        // Arrange
        using var _ = new SkipSignatureCheckScope();
        var sessionManager = new SessionManager(maxRequestsPerMinute: 1);
        var tool = new ConnectTool(sessionManager);
        var parameters = new { processId = 12345 };

        // Consume rate limit
        await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        // Act
        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        // Assert
        var json = JsonSerializer.SerializeToElement(result);
        json.TryGetProperty("availableTokens", out var tokensProp).Should().BeTrue(
            "rate limit response must include availableTokens for monitoring");
    }

    [Fact]
    public void ServerInstructions_RateLimitDescription_ShouldMatchActualResponseFields()
    {
        // The ServerInstructions text documents the rate-limit error response format.
        // It must mention all fields that tools actually return.
        var instructions = ServerInstructions.Value;

        instructions.Should().Contain("retryAfterSeconds",
            "ServerInstructions must document the numeric retryAfterSeconds field");
        instructions.Should().Contain("availableTokens",
            "ServerInstructions must document the availableTokens field");
    }
}
