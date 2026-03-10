using System.IO.Pipes;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Serialization;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public class InteractionToolMetadataTests
{
    [Fact]
    public async Task ExecuteCommand_ShouldIncludeRequestedInputAndObservedEffectMetadata()
    {
        const int processId = 51001;
        var inspectorResult = JsonSerializer.Serialize(new
        {
            success = true,
            commandName = "SaveCommand",
            executed = true,
            canExecute = true
        });

        using var connected = await CreateConnectedSessionAsync(processId, inspectorResult);
        var tool = new ExecuteCommandTool(connected.SessionManager);

        var result = await tool.ExecuteAsync(ToJsonElement(new
        {
            processId,
            elementId = "SaveButton",
            commandName = "SaveCommand",
            parameter = "Document-1"
        }), CancellationToken.None);

        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("requestedInput").GetProperty("commandName").GetString().Should().Be("SaveCommand");
        json.GetProperty("effectiveInput").GetProperty("parameter").GetString().Should().Be("Document-1");
        json.GetProperty("observedEffect").GetProperty("executed").GetBoolean().Should().BeTrue();
        json.GetProperty("usedFallback").GetBoolean().Should().BeFalse();
        json.GetProperty("notes").GetString().Should().Contain("real application logic");
    }

    [Fact]
    public async Task ClickElement_ShouldIncludeRequestedInputAndObservedEffectMetadata()
    {
        const int processId = 51002;
        var inspectorResult = JsonSerializer.Serialize(new
        {
            success = true,
            clicked = true
        });

        using var connected = await CreateConnectedSessionAsync(processId, inspectorResult);
        var tool = new ClickElementTool(connected.SessionManager);

        var result = await tool.ExecuteAsync(ToJsonElement(new
        {
            processId,
            elementId = "SaveButton"
        }), CancellationToken.None);

        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("requestedInput").GetProperty("elementId").GetString().Should().Be("SaveButton");
        json.GetProperty("observedEffect").GetProperty("clicked").GetBoolean().Should().BeTrue();
        json.GetProperty("usedFallback").GetBoolean().Should().BeFalse();
        json.GetProperty("notes").GetString().Should().Contain("real application logic");
    }

    [Fact]
    public async Task FireRoutedEvent_ShouldIncludeRequestedInputAndFallbackMetadata()
    {
        const int processId = 51003;
        var inspectorResult = JsonSerializer.Serialize(new
        {
            success = true,
            eventName = "Click",
            message = "Invoked OnClick path",
            usedOnClick = true
        });

        using var connected = await CreateConnectedSessionAsync(processId, inspectorResult);
        var tool = new FireRoutedEventTool(connected.SessionManager);

        var result = await tool.ExecuteAsync(ToJsonElement(new
        {
            processId,
            elementId = "SaveButton",
            eventName = "Click"
        }), CancellationToken.None);

        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("requestedInput").GetProperty("eventName").GetString().Should().Be("Click");
        json.GetProperty("observedEffect").GetProperty("usedOnClick").GetBoolean().Should().BeTrue();
        json.GetProperty("usedFallback").GetBoolean().Should().BeTrue();
        json.GetProperty("notes").GetString().Should().Contain("OnClick");
    }

    [Fact]
    public async Task ModifyViewModel_GenericPipeTool_ShouldIncludeRequestedInputAndObservedEffectMetadata()
    {
        const int processId = 51004;
        var inspectorResult = JsonSerializer.Serialize(new
        {
            success = true,
            propertyName = "Name",
            oldValue = "Alice",
            newValue = "Bob"
        });

        using var connected = await CreateConnectedSessionAsync(processId, inspectorResult);
        var tool = new GenericPipeTool(
            connected.SessionManager,
            "modify_viewmodel",
            GenericPipeTool.ExtractElementPropertyAndValueParams,
            GenericPipeTool.AugmentModifyViewModelResult);

        var result = await tool.ExecuteAsync(ToJsonElement(new
        {
            processId,
            elementId = "NameTextBox",
            propertyName = "Name",
            value = "Bob"
        }), CancellationToken.None);

        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("requestedInput").GetProperty("propertyName").GetString().Should().Be("Name");
        json.GetProperty("observedEffect").GetProperty("newValue").GetString().Should().Be("Bob");
        json.GetProperty("usedFallback").GetBoolean().Should().BeFalse();
        json.GetProperty("notes").GetString().Should().Contain("INotifyPropertyChanged");
    }

    private static async Task<ConnectedInteractionSession> CreateConnectedSessionAsync(int processId, string resultJson)
    {
        var pipeName = $"WpfDevTools_Test_{Guid.NewGuid():N}";
        var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();
            var requestJson = await MessageFraming.ReadMessageAsync(server, CancellationToken.None);
            var request = JsonSerializer.Deserialize<InspectorRequest>(requestJson);

            var response = new InspectorResponse
            {
                Id = request!.Id,
                CorrelationId = request.CorrelationId,
                Result = JsonSerializer.Deserialize<JsonElement>(resultJson),
                Error = null
            };

            await MessageFraming.WriteMessageAsync(
                server,
                JsonSerializer.Serialize(response),
                CancellationToken.None);
        });

        var sessionManager = new SessionManager();
        sessionManager.AddSession(processId);

        var client = new NamedPipeClient(processId, pipeName);
        var connected = await client.ConnectAsync(TimeSpan.FromSeconds(5), maxRetries: 1);
        connected.Should().BeTrue();

        ReplacePipeClient(sessionManager, processId, client);
        return new ConnectedInteractionSession(sessionManager, server, serverTask);
    }

    private static void ReplacePipeClient(SessionManager sessionManager, int processId, NamedPipeClient replacement)
    {
        var field = typeof(SessionManager).GetField("_pipeClients", BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();

        var pipeClients = field!.GetValue(sessionManager) as Dictionary<int, NamedPipeClient>;
        pipeClients.Should().NotBeNull();

        if (pipeClients!.TryGetValue(processId, out var existingClient))
        {
            existingClient.Dispose();
        }

        pipeClients[processId] = replacement;
    }

    private sealed class ConnectedInteractionSession(
        SessionManager sessionManager,
        NamedPipeServerStream server,
        Task serverTask) : IDisposable
    {
        public SessionManager SessionManager { get; } = sessionManager;

        public void Dispose()
        {
            try
            {
                serverTask.GetAwaiter().GetResult();
            }
            finally
            {
                SessionManager.Dispose();
                server.Dispose();
            }
        }
    }
}
