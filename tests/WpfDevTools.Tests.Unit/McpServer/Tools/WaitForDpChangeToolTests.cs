using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using Xunit;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public sealed class WaitForDpChangeToolTests
{
    [Fact]
    public async Task Execute_WithoutConnection_ShouldReturnError()
    {
        var tool = new WaitForDpChangeTool(new SessionManager());
        var parameters = new { processId = 12345, propertyName = "Width", timeoutMs = 100 };

        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().Contain("not connected");
    }

    [Fact]
    public async Task Execute_WithMissingPropertyName_ShouldReturnError()
    {
        var sessionManager = new SessionManager();
        sessionManager.AddSession(12345);
        var tool = new WaitForDpChangeTool(sessionManager);
        var parameters = new { processId = 12345, timeoutMs = 100 };

        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().Contain("propertyName");
    }

    [Fact]
    public async Task Execute_WithValidParameters_ShouldReturnResult()
    {
        var sessionManager = new SessionManager();
        sessionManager.AddSession(12345);
        var tool = new WaitForDpChangeTool(sessionManager);
        var parameters = new { processId = 12345, propertyName = "Width", elementId = "myButton", timeoutMs = 100, pollIntervalMs = 50 };

        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        result.Should().NotBeNull();
    }
}
