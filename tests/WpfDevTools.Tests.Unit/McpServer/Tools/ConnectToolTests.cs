using Xunit;
using FluentAssertions;
using System.Text.Json;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public class ConnectToolTests
{
    [Fact]
    public async Task Execute_WithInvalidProcessId_ShouldReturnError()
    {
        // Arrange
        var tool = new ConnectTool(new SessionManager());
        var parameters = new { processId = 999999 };

        // Act
        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Execute_WithMissingProcessId_ShouldReturnError()
    {
        // Arrange
        var tool = new ConnectTool(new SessionManager());
        var parameters = new { };

        // Act
        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().Contain("processId");
    }

    [Fact]
    public async Task Execute_WithNonWpfProcess_ShouldReturnError()
    {
        // Arrange
        var tool = new ConnectTool(new SessionManager());

        // Find a system process that is definitely not WPF (e.g., svchost, System, Idle)
        var systemProcesses = System.Diagnostics.Process.GetProcessesByName("svchost");
        if (systemProcesses.Length == 0)
        {
            // Fallback to other system processes
            systemProcesses = System.Diagnostics.Process.GetProcessesByName("System");
        }

        if (systemProcesses.Length == 0)
        {
            // Skip test if no suitable process found
            return;
        }

        var nonWpfProcessId = systemProcesses[0].Id;
        var parameters = new { processId = nonWpfProcessId };

        // Act
        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();

        // Cleanup
        foreach (var proc in systemProcesses)
        {
            proc.Dispose();
        }
    }
}
