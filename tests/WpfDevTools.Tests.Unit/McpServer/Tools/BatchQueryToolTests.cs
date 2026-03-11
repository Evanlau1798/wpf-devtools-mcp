using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public class BatchQueryToolTests
{
    [Fact]
    public async Task ExecuteAsync_WithSingleTarget_ShouldReturnSingleResponseShape()
    {
        var result = await BatchQueryExecutor.ExecuteAsync(
            new[] { "Button_1" },
            new[] { "Width" },
            (elementId, propertyName, _) => Task.FromResult<object>(new
            {
                success = true,
                propertyName,
                currentValue = 120
            }),
            CancellationToken.None);

        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("success").GetBoolean().Should().BeTrue();
        json.GetProperty("propertyName").GetString().Should().Be("Width");
        json.GetProperty("currentValue").GetInt32().Should().Be(120);
        json.TryGetProperty("results", out _).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WithBatchTargets_ShouldReturnCorrelatedResults()
    {
        var result = await BatchQueryExecutor.ExecuteAsync(
            new[] { "Button_1", "Button_2" },
            new[] { "Width", "Height" },
            (elementId, propertyName, _) => Task.FromResult<object>(new
            {
                success = true,
                propertyName,
                currentValue = $"{elementId}:{propertyName}"
            }),
            CancellationToken.None);

        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("success").GetBoolean().Should().BeTrue();
        json.GetProperty("resultCount").GetInt32().Should().Be(4);
        var results = json.GetProperty("results").EnumerateArray().ToArray();
        results.Should().HaveCount(4);
        results[0].GetProperty("elementId").GetString().Should().NotBeNullOrEmpty();
        results[0].GetProperty("propertyName").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetDpValueSourceTool_WithMixedSingleAndBatchInputs_ShouldReturnStructuredError()
    {
        var sessionManager = new SessionManager();
        sessionManager.AddSession(52001);
        var tool = new GetDpValueSourceTool(sessionManager);

        var result = await tool.ExecuteAsync(TestHelpers.ToJsonElement(new
        {
            processId = 52001,
            elementId = "Button_1",
            elementIds = new[] { "Button_2" },
            propertyName = "Width"
        }), CancellationToken.None);

        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("success").GetBoolean().Should().BeFalse();
        json.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
        json.GetProperty("error").GetString().Should().Contain("elementId");
        json.GetProperty("error").GetString().Should().Contain("elementIds");
    }
}
