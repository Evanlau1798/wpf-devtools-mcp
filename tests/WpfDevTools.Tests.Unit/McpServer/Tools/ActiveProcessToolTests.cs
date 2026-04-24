using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public class ActiveProcessToolTests
{
    [Fact]
    public async Task SelectActiveProcess_WithConnectedSession_ShouldReturnSuccess()
    {
        using var sessionManager = new SessionManager();
        sessionManager.AddSession(12345);
        var tool = new SelectActiveProcessTool(sessionManager);

        var result = await tool.ExecuteAsync(ToJsonElement(new { processId = 12345 }), CancellationToken.None);

        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("success").GetBoolean().Should().BeTrue();
        json.GetProperty("processId").GetInt32().Should().Be(12345);
    }

    [Fact]
    public async Task SelectActiveProcess_WithoutExistingSession_ShouldReturnStructuredError()
    {
        using var sessionManager = new SessionManager();
        var tool = new SelectActiveProcessTool(sessionManager);

        var result = await tool.ExecuteAsync(ToJsonElement(new { processId = 12345 }), CancellationToken.None);

        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("success").GetBoolean().Should().BeFalse();
        json.GetProperty("errorCode").GetString().Should().Be("NotConnected");
    }

    [Fact]
    public async Task GetActiveProcess_WithoutSelection_ShouldReturnEmptyState()
    {
        using var sessionManager = new SessionManager();
        var tool = new GetActiveProcessTool(sessionManager);

        var result = await tool.ExecuteAsync(null, CancellationToken.None);

        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("success").GetBoolean().Should().BeTrue();
        json.GetProperty("hasActiveProcess").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task GetActiveProcess_AfterInitialSessionSelection_ShouldReturnInjectedSelectedAtUtc()
    {
        var currentTime = new DateTimeOffset(2026, 4, 24, 11, 0, 0, TimeSpan.Zero);
        using var sessionManager = new SessionManager(
            McpServerConfiguration.RateLimitRequestsPerMinute,
            authManager: null,
            certManager: null,
            utcNowProvider: () => currentTime);
        sessionManager.AddSession(12345);
        var tool = new GetActiveProcessTool(sessionManager);

        var result = await tool.ExecuteAsync(null, CancellationToken.None);

        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("success").GetBoolean().Should().BeTrue();
        json.GetProperty("hasActiveProcess").GetBoolean().Should().BeTrue();
        json.GetProperty("processId").GetInt32().Should().Be(12345);
        json.GetProperty("selectedAtUtc").GetDateTimeOffset().Should().Be(currentTime);
    }

    [Fact]
    public async Task SelectActiveProcess_ShouldReturnInjectedSelectedAtUtcForExplicitSwitch()
    {
        var currentTime = new DateTimeOffset(2026, 4, 24, 11, 0, 0, TimeSpan.Zero);
        using var sessionManager = new SessionManager(
            McpServerConfiguration.RateLimitRequestsPerMinute,
            authManager: null,
            certManager: null,
            utcNowProvider: () => currentTime);
        sessionManager.AddSession(12345);
        currentTime = currentTime.AddMinutes(5);
        sessionManager.AddSession(23456);

        var selectTool = new SelectActiveProcessTool(sessionManager);
        var getTool = new GetActiveProcessTool(sessionManager);

        await selectTool.ExecuteAsync(ToJsonElement(new { processId = 23456 }), CancellationToken.None);
        var result = await getTool.ExecuteAsync(null, CancellationToken.None);

        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("success").GetBoolean().Should().BeTrue();
        json.GetProperty("hasActiveProcess").GetBoolean().Should().BeTrue();
        json.GetProperty("processId").GetInt32().Should().Be(23456);
        json.GetProperty("selectedAtUtc").GetDateTimeOffset().Should().Be(currentTime);
    }
}
