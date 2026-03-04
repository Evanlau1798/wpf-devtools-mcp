using Xunit;
using FluentAssertions;
using System.Text.Json;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public class GenericPipeToolTests
{
    [Fact]
    public async Task ExecuteAsync_WithValidParameters_ShouldForwardRequest()
    {
        // Arrange
        var sessionManager = new SessionManager();
        var tool = new GenericPipeTool(sessionManager, "test_method");
        var parameters = new { processId = 12345, elementId = "elem_123" };

        // Act
        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        var doc = System.Text.Json.JsonDocument.Parse(json);
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("error").GetString().Should().Contain("not connected");
    }

    [Fact]
    public async Task ExecuteAsync_WithMissingProcessId_ShouldReturnError()
    {
        // Arrange
        var sessionManager = new SessionManager();
        var tool = new GenericPipeTool(sessionManager, "test_method");
        var parameters = new { elementId = "elem_123" };

        // Act
        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        var doc = System.Text.Json.JsonDocument.Parse(json);
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("error").GetString().Should().Contain("processId");
    }

    [Fact]
    public async Task ExecuteAsync_WithCustomParamExtractor_ShouldUseExtractor()
    {
        // Arrange
        var sessionManager = new SessionManager();
        Func<JsonElement?, (int processId, object? parameters, object? error)> customExtractor = (JsonElement? args) =>
        {
            var processId = args?.GetProperty("pid").GetInt32() ?? 0;
            if (processId == 0)
                return (-1, null, new { success = false, error = "Missing pid" });
            return (processId, new { customParam = "value" }, null);
        };

        var tool = new GenericPipeTool(sessionManager, "test_method", customExtractor);
        var parameters = new { pid = 12345 };

        // Act
        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        var doc = System.Text.Json.JsonDocument.Parse(json);
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("error").GetString().Should().Contain("not connected");
    }

    [Fact]
    public async Task ExecuteAsync_WithNullArguments_ShouldReturnError()
    {
        // Arrange
        var sessionManager = new SessionManager();
        var tool = new GenericPipeTool(sessionManager, "test_method");

        // Act
        var result = await tool.ExecuteAsync(null, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        var doc = System.Text.Json.JsonDocument.Parse(json);
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
        doc.RootElement.GetProperty("error").GetString().Should().Contain("processId");
    }
}
