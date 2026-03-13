using Xunit;
using FluentAssertions;
using System.Text.Json;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.McpTools;
using WpfDevTools.Mcp.Server.Tools;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public sealed class GetBindingErrorsToolTests : IDisposable
{
    public void Dispose()
    {
        ToolCallHelper.ResetCacheForTesting();
    }

    [Fact]
    public async Task Execute_WithoutConnection_ShouldReturnError()
    {
        // Arrange
        var tool = new GetBindingErrorsTool(new SessionManager());
        var parameters = new { processId = 12345 };

        // Act
        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

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
        var tool = new GetBindingErrorsTool(new SessionManager());
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
    public async Task Execute_WithValidParameters_ShouldReturnPlaceholder()
    {
        // Arrange
        var sessionManager = new SessionManager();
        sessionManager.AddSession(12345);
        var tool = new GetBindingErrorsTool(sessionManager);
        var parameters = new { processId = 12345 };

        // Act
        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WithPathMismatch_ShouldSuggestDatacontextChainThenBindings()
    {
        var args = ToolCallHelper.BuildJsonArgs(("processId", 12345), ("elementId", "TextBox_1"));

        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(new
            {
                success = true,
                errorCount = 1,
                errors = new[]
                {
                    new
                    {
                        message = "BindingExpression path error: 'MissingName' property not found on object.",
                        eventType = "PathError",
                        elementId = "TextBox_1",
                        propertyName = "Text",
                        bindingPath = "MissingName"
                    }
                }
            }),
            args,
            CancellationToken.None,
            toolName: "get_binding_errors");

        var nextSteps = result.StructuredContent!.Value.GetProperty("nextSteps");
        nextSteps.GetArrayLength().Should().Be(2);
        nextSteps[0].GetProperty("tool").GetString().Should().Be("get_datacontext_chain");
        nextSteps[0].GetProperty("params").GetProperty("elementId").GetString().Should().Be("TextBox_1");
        nextSteps[1].GetProperty("tool").GetString().Should().Be("get_bindings");
        nextSteps[1].GetProperty("params").GetProperty("elementId").GetString().Should().Be("TextBox_1");
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WithConverterFailure_ShouldSuggestBindingValueChain()
    {
        var args = ToolCallHelper.BuildJsonArgs(("processId", 12345), ("elementId", "TextBox_2"));

        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(new
            {
                success = true,
                errorCount = 1,
                errors = new[]
                {
                    new
                    {
                        message = "Cannot convert value for target property during UpdateTarget.",
                        eventType = "UpdateTargetError",
                        elementId = "TextBox_2",
                        propertyName = "Text",
                        bindingPath = "Age"
                    }
                }
            }),
            args,
            CancellationToken.None,
            toolName: "get_binding_errors");

        var nextSteps = result.StructuredContent!.Value.GetProperty("nextSteps");
        nextSteps.GetArrayLength().Should().Be(1);
        nextSteps[0].GetProperty("tool").GetString().Should().Be("get_binding_value_chain");
        nextSteps[0].GetProperty("params").GetProperty("elementId").GetString().Should().Be("TextBox_2");
        nextSteps[0].GetProperty("params").GetProperty("propertyName").GetString().Should().Be("Text");
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WithoutElementIdentity_ShouldNotEmitElementSpecificGuidance()
    {
        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(new
            {
                success = true,
                errorCount = 1,
                errors = new[]
                {
                    new
                    {
                        message = "BindingExpression path error: 'MissingName' property not found on object.",
                        eventType = "PathError",
                        elementId = (string?)null,
                        propertyName = "Text",
                        bindingPath = "MissingName"
                    }
                }
            }),
            ToolCallHelper.BuildJsonArgs(("processId", 12345)),
            CancellationToken.None,
            toolName: "get_binding_errors");

        result.StructuredContent!.Value.GetProperty("nextSteps").GetArrayLength().Should().Be(0);
    }
}
