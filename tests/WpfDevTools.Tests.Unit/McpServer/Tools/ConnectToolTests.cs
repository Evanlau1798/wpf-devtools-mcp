using Xunit;
using FluentAssertions;
using System.Text.Json;
using WpfDevTools.Mcp.Server.Tools;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public class ConnectToolTests
{
    [Fact]
    public async Task Execute_WithInvalidProcessId_ShouldReturnError()
    {
        // Arrange
        var tool = new ConnectTool();
        var parameters = new { processId = 999999 };

        // Act
        var result = await tool.ExecuteAsync(parameters, CancellationToken.None);

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
        var tool = new ConnectTool();
        var parameters = new { };

        // Act
        var result = await tool.ExecuteAsync(parameters, CancellationToken.None);

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
        var tool = new ConnectTool();
        var currentProcessId = System.Diagnostics.Process.GetCurrentProcess().Id;
        var parameters = new { processId = currentProcessId };

        // Act
        var result = await tool.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
    }
}
