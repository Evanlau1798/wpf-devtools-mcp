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
}
