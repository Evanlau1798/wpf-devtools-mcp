using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Inspector.Host;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer;

public class PipeConnectedToolBehaviorTests : IDisposable
{
    private readonly SessionManager _sessionManager = new();

    public void Dispose()
    {
        _sessionManager.Dispose();
    }

    [Fact]
    public async Task GenericPipeTool_Execute_WithConnectedSession_ShouldRefreshLastActivity()
    {
        var processId = Random.Shared.Next(100_000, 999_999);
        using var host = new InspectorHost(processId);
        host.Start();

        _sessionManager.AddSession(processId);
        var client = _sessionManager.GetPipeClient(processId);

        client.Should().NotBeNull();
        (await client!.ConnectAsync(TimeSpan.FromSeconds(5), maxRetries: 1)).Should().BeTrue();

        var initialActivity = _sessionManager.GetLastActivityTime(processId);
        await Task.Delay(50);

        var tool = new GenericPipeTool(_sessionManager, "ping");
        var result = await tool.ExecuteAsync(ToJsonElement(new { processId }), CancellationToken.None);

        JsonSerializer.Serialize(result).Should().Contain("\"success\":true");
        _sessionManager.GetLastActivityTime(processId).Should().BeAfter(initialActivity);
    }

    [Fact]
    public async Task GenericPipeTool_Execute_WhenInspectorReturnsError_ShouldPreserveErrorCode()
    {
        var processId = Random.Shared.Next(100_000, 999_999);
        using var host = new InspectorHost(processId);
        host.Start();

        _sessionManager.AddSession(processId);
        var client = _sessionManager.GetPipeClient(processId);

        client.Should().NotBeNull();
        (await client!.ConnectAsync(TimeSpan.FromSeconds(5), maxRetries: 1)).Should().BeTrue();

        var tool = new GenericPipeTool(_sessionManager, "nonexistent_method");
        var result = await tool.ExecuteAsync(ToJsonElement(new { processId }), CancellationToken.None);
        var resultJson = JsonSerializer.SerializeToElement(result);

        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().Contain("Method not found");
        resultJson.GetProperty("errorCode").GetString().Should().Be("MethodNotFound");
    }

    [Fact]
    public async Task GenericPipeTool_Execute_WithDisconnectedPipe_ShouldReturnStructuredNotConnectedError()
    {
        var processId = Random.Shared.Next(100_000, 999_999);
        _sessionManager.AddSession(processId);

        var tool = new GenericPipeTool(_sessionManager, "ping");
        var result = await tool.ExecuteAsync(ToJsonElement(new { processId }), CancellationToken.None);
        var resultJson = JsonSerializer.SerializeToElement(result);

        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("errorCode").GetString().Should().Be("NotConnected");
        resultJson.GetProperty("hint").GetString().Should().Contain("connect");
    }
}
