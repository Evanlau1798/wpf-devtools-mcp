using Xunit;
using FluentAssertions;
using System.Text.Json;
using WpfDevTools.Mcp.Server;

namespace WpfDevTools.Tests.Unit.McpServer;

public class McpProtocolHandlerTests
{
    [Fact]
    public async Task HandleRequest_WithInitialize_ShouldReturnServerInfo()
    {
        // Arrange
        var handler = new McpProtocolHandler();
        var request = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new
            {
                protocolVersion = "2024-11-05",
                clientInfo = new { name = "test-client", version = "1.0" }
            }
        });

        // Act
        var response = await handler.HandleRequestAsync(request, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        var responseObj = JsonSerializer.Deserialize<JsonElement>(response);
        responseObj.GetProperty("jsonrpc").GetString().Should().Be("2.0");
        responseObj.GetProperty("id").GetInt32().Should().Be(1);
        responseObj.GetProperty("result").GetProperty("protocolVersion").GetString().Should().Be("2024-11-05");
        responseObj.GetProperty("result").GetProperty("serverInfo").GetProperty("name").GetString().Should().Be("wpf-devtools-mcp");
    }

    [Fact]
    public async Task HandleRequest_WithToolsList_ShouldReturnRegisteredTools()
    {
        // Arrange
        var registry = new ToolRegistry();
        registry.RegisterTool(new ToolDefinition
        {
            Name = "ping",
            Description = "Test connectivity",
            Parameters = new { }
        });

        var handler = new McpProtocolHandler(registry);
        var request = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 2,
            method = "tools/list"
        });

        // Act
        var response = await handler.HandleRequestAsync(request, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        var responseObj = JsonSerializer.Deserialize<JsonElement>(response);
        responseObj.GetProperty("result").GetProperty("tools").GetArrayLength().Should().Be(1);
        responseObj.GetProperty("result").GetProperty("tools")[0].GetProperty("name").GetString().Should().Be("ping");
    }

    [Fact]
    public async Task HandleRequest_WithInvalidJson_ShouldReturnParseError()
    {
        // Arrange
        var handler = new McpProtocolHandler();
        var request = "{ invalid json }";

        // Act
        var response = await handler.HandleRequestAsync(request, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        var responseObj = JsonSerializer.Deserialize<JsonElement>(response);
        responseObj.GetProperty("error").GetProperty("code").GetInt32().Should().Be(-32700); // Parse error
    }

    [Fact]
    public async Task HandleRequest_WithUnknownMethod_ShouldReturnMethodNotFound()
    {
        // Arrange
        var handler = new McpProtocolHandler();
        var request = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 3,
            method = "unknown/method"
        });

        // Act
        var response = await handler.HandleRequestAsync(request, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        var responseObj = JsonSerializer.Deserialize<JsonElement>(response);
        responseObj.GetProperty("error").GetProperty("code").GetInt32().Should().Be(-32601); // Method not found
    }
}
