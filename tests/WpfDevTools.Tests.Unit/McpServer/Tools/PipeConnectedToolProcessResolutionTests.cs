using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public class PipeConnectedToolProcessResolutionTests
{
    [Fact]
    public async Task Execute_WhenProcessIdOmitted_ShouldUseActiveProcess()
    {
        using var sessionManager = new SessionManager();
        sessionManager.AddSession(12345);
        var tool = new ProcessResolvingTestTool(sessionManager);

        var result = await tool.ExecuteAsync(ToJsonElement(new { elementId = "Button_1" }), CancellationToken.None);

        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("success").GetBoolean().Should().BeTrue();
        json.GetProperty("resolvedProcessId").GetInt32().Should().Be(12345);
    }

    [Fact]
    public async Task Execute_WhenProcessIdOmittedAndNoActiveProcess_ShouldReturnStructuredError()
    {
        using var sessionManager = new SessionManager();
        var tool = new ProcessResolvingTestTool(sessionManager);

        var result = await tool.ExecuteAsync(ToJsonElement(new { elementId = "Button_1" }), CancellationToken.None);

        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("success").GetBoolean().Should().BeFalse();
        json.GetProperty("errorCode").GetString().Should().Be("NoActiveProcess");
    }

    [Fact]
    public async Task Execute_WhenProcessIdIsMalformed_ShouldNotFallbackToActiveProcess()
    {
        using var sessionManager = new SessionManager();
        sessionManager.AddSession(12345);
        var tool = new ProcessResolvingTestTool(sessionManager);

        var result = await tool.ExecuteAsync(ToJsonElement(new { processId = "not-an-int", elementId = "Button_1" }), CancellationToken.None);

        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("success").GetBoolean().Should().BeFalse();
        json.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
        json.GetProperty("error").GetString().Should().Contain("processId");
    }

    private sealed class ProcessResolvingTestTool(SessionManager sessionManager) : PipeConnectedToolBase(sessionManager)
    {
        public Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
        {
            var (processId, elementId, error) = ParseCommonParams(arguments, _sessionManager);
            if (error is not null)
            {
                return Task.FromResult(error);
            }

            return Task.FromResult<object>(new
            {
                success = true,
                resolvedProcessId = processId,
                resolvedElementId = elementId
            });
        }
    }
}
