using System.IO.Pipes;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Serialization;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public sealed class ProcessBoundToolForwardingTests
{
    [Fact]
    public async Task ElementScopedTools_ShouldForwardMethodAndTargetOverConnectedPipe()
    {
        var processId = NextSyntheticProcessId();
        var operations = CreateOperations(processId);
        var pipeName = $"WpfDevTools_Forwarding_{Guid.NewGuid():N}";
        using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        using var sessionManager = new SessionManager();
        DisableSessionManagerCleanupTimer(sessionManager);
        sessionManager.AddSession(processId);

        var serverTask = ServeAndAssertAsync(server, operations);
        var client = new NamedPipeClient(processId, pipeName);
        (await client.ConnectAsync(TimeSpan.FromSeconds(5), maxRetries: 1)).Should().BeTrue();
        ReplaceSessionManagerPipeClient(sessionManager, processId, client);

        foreach (var operation in operations)
        {
            await operation.Execute(sessionManager);
        }

        await serverTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static IReadOnlyList<ToolOperation> CreateOperations(int processId)
    {
        var target = "target-element";
        return
        [
            new("click_element", target, null, null, manager => new ClickElementTool(manager).ExecuteAsync(Args(processId, target), CancellationToken.None)),
            new("get_applied_styles", target, null, null, manager => new GetAppliedStylesTool(manager).ExecuteAsync(Args(processId, target), CancellationToken.None)),
            new("get_commands", target, null, null, manager => new GetCommandsTool(manager).ExecuteAsync(Args(processId, target), CancellationToken.None)),
            new("get_clipping_info", target, null, null, manager => new GetClippingInfoTool(manager).ExecuteAsync(Args(processId, target), CancellationToken.None)),
            new("get_bindings", target, null, null, manager => new GetBindingsTool(manager).ExecuteAsync(Args(processId, target), CancellationToken.None)),
            new("get_datacontext_chain", target, null, null, manager => new GetDataContextChainTool(manager).ExecuteAsync(Args(processId, target), CancellationToken.None)),
            new("get_event_handlers", target, "eventName", "Click", manager => new GetEventHandlersTool(manager).ExecuteAsync(Args(processId, target, "eventName", "Click"), CancellationToken.None)),
            new("get_layout_info", target, null, null, manager => new GetLayoutInfoTool(manager).ExecuteAsync(Args(processId, target), CancellationToken.None)),
            new("get_resource_chain", target, "resourceKey", "AccentBrush", manager => new GetResourceChainTool(manager).ExecuteAsync(Args(processId, target, "resourceKey", "AccentBrush"), CancellationToken.None)),
            new("get_triggers", target, null, null, manager => new GetTriggersTool(manager).ExecuteAsync(Args(processId, target), CancellationToken.None)),
            new("get_validation_errors", target, null, null, manager => new GetValidationErrorsTool(manager).ExecuteAsync(Args(processId, target), CancellationToken.None)),
            new("get_viewmodel", target, null, null, manager => new GetViewModelTool(manager).ExecuteAsync(Args(processId, target), CancellationToken.None)),
            new("get_visual_count", target, null, null, manager => new GetVisualCountTool(manager).ExecuteAsync(Args(processId, target), CancellationToken.None)),
            new("invalidate_layout", target, null, null, manager => new InvalidateLayoutTool(manager).ExecuteAsync(Args(processId, target), CancellationToken.None)),
            new("scroll_to_element", target, null, null, manager => new ScrollToElementTool(manager).ExecuteAsync(Args(processId, target), CancellationToken.None))
        ];
    }

    private static JsonElement Args(
        int processId,
        string elementId,
        string? extraName = null,
        string? extraValue = null)
    {
        var values = new Dictionary<string, object?>
        {
            ["processId"] = processId,
            ["elementId"] = elementId
        };
        if (extraName is not null)
        {
            values[extraName] = extraValue;
        }

        return JsonSerializer.SerializeToElement(values);
    }

    private static async Task ServeAndAssertAsync(
        NamedPipeServerStream server,
        IReadOnlyList<ToolOperation> operations)
    {
        await server.WaitForConnectionAsync();
        var operationIndex = 0;
        while (operationIndex < operations.Count)
        {
            var requestJson = await MessageFraming.ReadMessageAsync(server, CancellationToken.None);
            var request = JsonSerializer.Deserialize<InspectorRequest>(requestJson)!;
            if (request.Method == "drain_events")
            {
                await WriteSuccessAsync(server, request);
                continue;
            }

            var operation = operations[operationIndex++];
            request.Method.Should().Be(operation.ExpectedMethod);
            request.Params.Should().NotBeNull();
            var payload = request.Params!.Value;
            payload.GetProperty("elementId").GetString().Should().Be(operation.ElementId);
            if (operation.ExtraName is not null)
            {
                payload.GetProperty(operation.ExtraName).GetString().Should().Be(operation.ExtraValue);
            }

            await WriteSuccessAsync(server, request);
        }
    }

    private static Task WriteSuccessAsync(NamedPipeServerStream server, InspectorRequest request)
    {
        var response = new InspectorResponse
        {
            Id = request.Id,
            CorrelationId = request.CorrelationId,
            Result = JsonSerializer.SerializeToElement(new { success = true })
        };
        return MessageFraming.WriteMessageAsync(
            server,
            JsonSerializer.Serialize(response),
            CancellationToken.None);
    }

    private sealed record ToolOperation(
        string ExpectedMethod,
        string ElementId,
        string? ExtraName,
        string? ExtraValue,
        Func<SessionManager, Task<object>> Execute);
}
