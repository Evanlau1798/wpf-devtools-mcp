using Xunit;
using FluentAssertions;
using System.Text.Json;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public class GetRenderStatsToolTests
{
    [Fact]
    public async Task Execute_WithoutConnection_ShouldReturnError()
    {
        // Arrange
        var tool = new GetRenderStatsTool();
        var parameters = new { processId = 12345 };

        // Act
        var result = await tool.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().Contain("not connected");
    }

    [Fact]
    public async Task Execute_WithMissingProcessId_ShouldReturnError()
    {
        // Arrange
        var tool = new GetRenderStatsTool();
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
    public async Task Execute_WithValidParameters_ShouldReturnPlaceholder()
    {
        // Arrange
        var sessionManager = new SessionManager();
        sessionManager.AddSession(12345);
        var tool = new GetRenderStatsTool(sessionManager);
        var parameters = new { processId = 12345 };

        // Act
        var result = await tool.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        // Note: This will return placeholder until Named Pipe communication is implemented
        // For now, we're just testing the parameter parsing
    }
}
