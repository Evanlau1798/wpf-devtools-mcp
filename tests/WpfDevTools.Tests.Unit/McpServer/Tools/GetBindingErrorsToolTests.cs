using System.IO.Pipes;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.McpTools;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Serialization;
using static WpfDevTools.Tests.Unit.TestHelpers;
using Xunit;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

[Collection("ToolCallHelperState")]
public sealed class GetBindingErrorsToolTests : IDisposable
{
    private readonly IDisposable _toolCallHelperScope = ToolCallHelper.BeginTestScope();

    public void Dispose()
    {
        _toolCallHelperScope.Dispose();
    }

    [Fact]
    public async Task Execute_WithoutConnection_ShouldReturnError()
    {
        var tool = new GetBindingErrorsTool(new SessionManager());
        var parameters = new { processId = 12345 };

        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        result.Should().NotBeNull();
        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().Contain("not connected");
    }

    [Fact]
    public async Task Execute_WithMissingProcessId_ShouldReturnError()
    {
        var tool = new GetBindingErrorsTool(new SessionManager());
        var parameters = new { };

        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        result.Should().NotBeNull();
        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().Contain("processId");
    }

    [Fact]
    public async Task Execute_WithValidParameters_ShouldReturnPlaceholder()
    {
        var sessionManager = new SessionManager();
        sessionManager.AddSession(12345);
        var tool = new GetBindingErrorsTool(sessionManager);
        var parameters = new { processId = 12345 };

        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task Execute_ShouldRequestVerboseInspectorPayload_WhenCallerOmitsCompact()
    {
        const int processId = 51041;
        var pipeName = $"WpfDevTools_Test_{Guid.NewGuid():N}";
        using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        JsonElement? observedParams = null;

        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();
            var requestJson = await MessageFraming.ReadMessageAsync(server, CancellationToken.None);
            var request = JsonSerializer.Deserialize<InspectorRequest>(requestJson);
            request.Should().NotBeNull();
            observedParams = request!.Params;

            var response = new InspectorResponse
            {
                Id = request.Id,
                CorrelationId = request.CorrelationId,
                Result = JsonSerializer.SerializeToElement(new
                {
                    success = true,
                    errorCount = 1,
                    errors = new[]
                    {
                        new
                        {
                            message = "BindingExpression path error: 'MissingName' property not found on object.",
                            eventType = "PathError",
                            elementId = "TextBox_4",
                            propertyName = "Text",
                            bindingPath = "MissingName"
                        }
                    }
                })
            };

            await MessageFraming.WriteMessageAsync(server, JsonSerializer.Serialize(response), CancellationToken.None);
        });

        using var sessionManager = new SessionManager();
        sessionManager.AddSession(processId);
        var client = new NamedPipeClient(processId, pipeName);
        (await client.ConnectAsync(TimeSpan.FromSeconds(5), maxRetries: 1)).Should().BeTrue();
        ReplacePipeClient(sessionManager, processId, client);
        var tool = new GetBindingErrorsTool(sessionManager);

        var result = JsonSerializer.SerializeToElement(await tool.ExecuteAsync(
            ToJsonElement(new { processId }),
            CancellationToken.None));

        await serverTask;

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        observedParams.Should().NotBeNull();
        observedParams!.Value.TryGetProperty("compact", out var compact).Should().BeTrue();
        compact.GetBoolean().Should().BeFalse("server-side navigation still needs the verbose inspector payload before trimming");
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

        var navigation = result.StructuredContent!.Value.GetProperty("navigation");
        var nextSteps = result.StructuredContent!.Value.GetProperty("nextSteps");
        nextSteps.GetArrayLength().Should().Be(1);
        nextSteps[0].GetProperty("tool").GetString().Should().Be("get_datacontext_chain");
        nextSteps[0].GetProperty("params").GetProperty("elementId").GetString().Should().Be("TextBox_1");
        navigation.GetProperty("alternatives")[0].GetProperty("tool").GetString().Should().Be("get_bindings");
        navigation.GetProperty("alternatives")[0].GetProperty("params").GetProperty("elementId").GetString().Should().Be("TextBox_1");
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

    [Fact]
    public async Task ExecuteAndWrapAsync_WithSuggestedElementIdentity_ShouldEmitElementSpecificGuidance()
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
                        suggestedElementId = "TextBox_1",
                        matchConfidence = "high",
                        propertyName = "Text",
                        bindingPath = "MissingName"
                    }
                }
            }),
            ToolCallHelper.BuildJsonArgs(("processId", 12345)),
            CancellationToken.None,
            toolName: "get_binding_errors");

        var nextSteps = result.StructuredContent!.Value.GetProperty("nextSteps");
        nextSteps.GetArrayLength().Should().Be(1);
        nextSteps[0].GetProperty("tool").GetString().Should().Be("get_datacontext_chain");
        nextSteps[0].GetProperty("params").GetProperty("elementId").GetString().Should().Be("TextBox_1");
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WithCompactResponse_ShouldTrimMessageAfterNavigationPlanning()
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
                        eventType = "Error",
                        elementId = "TextBox_3",
                        propertyName = "Text",
                        bindingPath = "MissingName"
                    }
                }
            }),
            ToolCallHelper.BuildJsonArgs(("processId", 12345), ("compact", true)),
            CancellationToken.None,
            toolName: "get_binding_errors");

        var navigation = result.StructuredContent!.Value.GetProperty("navigation");
        var nextSteps = result.StructuredContent!.Value.GetProperty("nextSteps");
        nextSteps.GetArrayLength().Should().Be(1);
        nextSteps[0].GetProperty("tool").GetString().Should().Be("get_datacontext_chain");
        result.StructuredContent!.Value.GetProperty("errors")[0].TryGetProperty("message", out _).Should().BeFalse();
        navigation.GetProperty("alternatives")[0].GetProperty("tool").GetString().Should().Be("get_bindings");
    }

    private static void ReplacePipeClient(SessionManager sessionManager, int processId, NamedPipeClient replacement)
    {
        ReplaceSessionManagerPipeClient(sessionManager, processId, replacement);
    }
}
