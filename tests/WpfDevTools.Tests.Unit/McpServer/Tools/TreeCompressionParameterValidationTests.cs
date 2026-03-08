using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using Xunit;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public class TreeCompressionParameterValidationTests
{
    [Fact]
    public async Task GetLogicalTree_WithMaxNodesZero_ShouldReturnValidationError()
    {
        var tool = new GetLogicalTreeTool(new SessionManager());
        var parameters = new { processId = 12345, maxNodes = 0 };

        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        var json = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        json.GetProperty("success").GetBoolean().Should().BeFalse();
        json.GetProperty("error").GetString().Should().Contain("maxNodes");
    }

    [Fact]
    public async Task GetLogicalTree_WithMaxChildrenPerNodeZero_ShouldReturnValidationError()
    {
        var tool = new GetLogicalTreeTool(new SessionManager());
        var parameters = new { processId = 12345, maxChildrenPerNode = 0 };

        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        var json = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        json.GetProperty("success").GetBoolean().Should().BeFalse();
        json.GetProperty("error").GetString().Should().Contain("maxChildrenPerNode");
    }

    [Fact]
    public async Task GetVisualTree_WithMaxNodesOverLimit_ShouldReturnValidationError()
    {
        var tool = new GetVisualTreeTool(new SessionManager());
        var parameters = new { processId = 12345, maxNodes = 10001 };

        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        var json = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        json.GetProperty("success").GetBoolean().Should().BeFalse();
        json.GetProperty("error").GetString().Should().Contain("maxNodes");
    }

    [Fact]
    public async Task GetVisualTree_WithValidCompressionParameters_ShouldReachConnectionCheck()
    {
        var tool = new GetVisualTreeTool(new SessionManager());
        var parameters = new
        {
            processId = 12345,
            compact = true,
            summaryOnly = true,
            maxNodes = 10,
            maxChildrenPerNode = 2
        };

        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        var json = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        json.GetProperty("success").GetBoolean().Should().BeFalse();
        json.GetProperty("error").GetString().Should().Contain("not connected");
    }
}