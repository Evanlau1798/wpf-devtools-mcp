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

public class MutationToolMetadataTests
{
    [Fact]
    public async Task SetDpValue_ShouldIncludeRequestedInputAndObservedEffectMetadata()
    {
        const int processId = 50001;
        var inspectorResult = JsonSerializer.Serialize(new
        {
            success = true,
            propertyName = "Width",
            oldValue = 50,
            newValue = 100
        });

        using var connected = await CreateConnectedSessionAsync(processId, inspectorResult);
        var tool = new SetDpValueTool(connected.SessionManager);

        var result = await tool.ExecuteAsync(ToJsonElement(new
        {
            processId,
            elementId = "Button_1",
            propertyName = "Width",
            value = 100
        }), CancellationToken.None);

        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("requestedInput").GetProperty("propertyName").GetString().Should().Be("Width");
        json.GetProperty("effectiveInput").GetProperty("elementId").GetString().Should().Be("Button_1");
        json.GetProperty("observedEffect").GetProperty("oldValue").GetInt32().Should().Be(50);
        json.GetProperty("usedFallback").GetBoolean().Should().BeFalse();
        json.GetProperty("notes").GetString().Should().Contain("Runtime-only");
    }

    [Fact]
    public async Task ClearDpValue_ShouldIncludeRequestedInputAndObservedEffectMetadata()
    {
        const int processId = 50002;
        var inspectorResult = JsonSerializer.Serialize(new
        {
            success = true,
            propertyName = "Width",
            hadLocalValue = true,
            clearedValue = 100,
            newValue = 50
        });

        using var connected = await CreateConnectedSessionAsync(processId, inspectorResult);
        var tool = new ClearDpValueTool(connected.SessionManager);

        var result = await tool.ExecuteAsync(ToJsonElement(new
        {
            processId,
            elementId = "Button_1",
            propertyName = "Width"
        }), CancellationToken.None);

        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("requestedInput").GetProperty("propertyName").GetString().Should().Be("Width");
        json.GetProperty("effectiveInput").GetProperty("elementId").GetString().Should().Be("Button_1");
        json.GetProperty("observedEffect").GetProperty("hadLocalValue").GetBoolean().Should().BeTrue();
        json.GetProperty("usedFallback").GetBoolean().Should().BeFalse();
        json.GetProperty("notes").GetString().Should().Contain("manual restore");
    }

    [Fact]
    public async Task OverrideStyleSetter_ShouldIncludeRequestedInputAndObservedEffectMetadata()
    {
        const int processId = 50003;
        var inspectorResult = JsonSerializer.Serialize(new
        {
            success = true,
            propertyName = "Background",
            oldValue = "Blue",
            newValue = "Red"
        });

        using var connected = await CreateConnectedSessionAsync(processId, inspectorResult);
        var tool = new OverrideStyleSetterTool(connected.SessionManager);

        var result = await tool.ExecuteAsync(ToJsonElement(new
        {
            processId,
            elementId = "Button_1",
            propertyName = "Background",
            value = "Red"
        }), CancellationToken.None);

        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("requestedInput").GetProperty("propertyName").GetString().Should().Be("Background");
        json.GetProperty("effectiveInput").GetProperty("value").GetString().Should().Be("Red");
        json.GetProperty("observedEffect").GetProperty("newValue").GetString().Should().Be("Red");
        json.GetProperty("usedFallback").GetBoolean().Should().BeFalse();
        json.GetProperty("notes").GetString().Should().Contain("style");
    }

    private static async Task<ConnectedMutationSession> CreateConnectedSessionAsync(int processId, string resultJson)
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
        return new ConnectedMutationSession(sessionManager, server, serverTask);
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

    private sealed class ConnectedMutationSession(
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
