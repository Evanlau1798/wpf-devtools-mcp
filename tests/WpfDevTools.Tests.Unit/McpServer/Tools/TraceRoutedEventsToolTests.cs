using System.IO.Pipes;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Navigation;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Serialization;
using Xunit;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public class TraceRoutedEventsToolTests
{
    [Fact]
    public async Task Execute_WithoutConnection_ShouldReturnError()
    {
        var tool = new TraceRoutedEventsTool(new SessionManager());
        var parameters = new { processId = 12345, eventName = "Click" };

        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        result.Should().NotBeNull();
        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().Contain("not connected");
    }

    [Fact]
    public async Task Execute_WithMissingProcessId_ShouldReturnError()
    {
        var tool = new TraceRoutedEventsTool(new SessionManager());
        var parameters = new { eventName = "Click" };

        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        result.Should().NotBeNull();
        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().Contain("processId");
    }

    [Fact]
    public async Task Execute_WithMissingEventName_ShouldReturnError()
    {
        var sessionManager = new SessionManager();
        sessionManager.AddSession(12345);
        var tool = new TraceRoutedEventsTool(sessionManager);
        var parameters = new { processId = 12345 };

        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        result.Should().NotBeNull();
        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().Contain("eventName");
    }

    [Fact]
    public async Task Execute_WithGetMode_ShouldNotRequireEventName()
    {
        var tool = new TraceRoutedEventsTool(new SessionManager());
        var parameters = new { processId = 12345, mode = "get" };

        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().Contain("not connected");
        resultJson.GetProperty("error").GetString().Should().NotContain("eventName");
    }

    [Fact]
    public async Task Execute_WithValidParameters_ShouldIncludeEventNameAndElementId()
    {
        var sessionManager = new SessionManager();
        sessionManager.AddSession(12345);
        var tool = new TraceRoutedEventsTool(sessionManager);
        var parameters = new { processId = 12345, eventName = "Click", elementId = "myButton" };

        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task Execute_WithStartMode_ShouldStoreActiveTraceState()
    {
        const int processId = 22001;
        using var connected = await CreateConnectedSessionAsync(
            processId,
            """{"success":true,"mode":"start","eventName":"Click","isTracing":true,"effectiveDuration":30000}""");
        var tool = new TraceRoutedEventsTool(connected.SessionManager);

        var result = await tool.ExecuteAsync(ToJsonElement(new
        {
            processId,
            eventName = "Click",
            elementId = "SaveButton",
            mode = "start"
        }), CancellationToken.None);

        JsonSerializer.SerializeToElement(result).GetProperty("success").GetBoolean().Should().BeTrue();
        connected.SessionManager.TryGetNavigationState(processId, out var state).Should().BeTrue();
        state!.ActiveTrace.Should().NotBeNull();
        state.ActiveTrace!.EventName.Should().Be("Click");
        state.ActiveTrace.ElementId.Should().Be("SaveButton");
        state.ActiveTrace.EffectiveDuration.Should().Be(TimeSpan.FromMilliseconds(30000));
    }

    [Fact]
    public async Task Execute_WithCompletedGetMode_ShouldClearActiveTraceState()
    {
        const int processId = 22002;
        using var connected = await CreateConnectedSessionAsync(
            processId,
            """{"success":true,"mode":"get","isTracing":false,"eventCount":0,"events":[]}""");
        connected.SessionManager.SetActiveTraceState(
            processId,
            new ActiveTraceNavigationState("Click", "SaveButton", DateTimeOffset.UtcNow));
        var tool = new TraceRoutedEventsTool(connected.SessionManager);

        var result = await tool.ExecuteAsync(ToJsonElement(new
        {
            processId,
            mode = "get"
        }), CancellationToken.None);

        JsonSerializer.SerializeToElement(result).GetProperty("success").GetBoolean().Should().BeTrue();
        connected.SessionManager.TryGetNavigationState(processId, out var state).Should().BeTrue();
        state!.ActiveTrace.Should().BeNull();
    }

    private static async Task<ConnectedTraceSession> CreateConnectedSessionAsync(int processId, string responseJson)
    {
        var pipeName = $"WpfDevTools_Test_{Guid.NewGuid():N}";
        var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();
            try
            {
                while (server.IsConnected)
                {
                    var requestJson = await MessageFraming.ReadMessageAsync(server, CancellationToken.None);
                    var request = JsonSerializer.Deserialize<InspectorRequest>(requestJson);
                    request.Should().NotBeNull();

                    var response = new InspectorResponse
                    {
                        Id = request!.Id,
                        CorrelationId = request.CorrelationId,
                        Result = JsonSerializer.Deserialize<JsonElement>(responseJson)
                    };

                    await MessageFraming.WriteMessageAsync(server, JsonSerializer.Serialize(response), CancellationToken.None);
                }
            }
            catch (EndOfStreamException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        });

        var sessionManager = new SessionManager();
        sessionManager.AddSession(processId);
        var client = new NamedPipeClient(processId, pipeName);
        (await client.ConnectAsync(TimeSpan.FromSeconds(5), maxRetries: 1)).Should().BeTrue();
        ReplacePipeClient(sessionManager, processId, client);

        return new ConnectedTraceSession(sessionManager, server, serverTask);
    }

    private static void ReplacePipeClient(SessionManager sessionManager, int processId, NamedPipeClient replacement)
    {
        var field = typeof(SessionManager).GetField("_pipeClients", BindingFlags.Instance | BindingFlags.NonPublic);
        var pipeClients = field!.GetValue(sessionManager) as Dictionary<int, NamedPipeClient>;
        if (pipeClients!.TryGetValue(processId, out var existingClient))
        {
            existingClient.Dispose();
        }

        pipeClients[processId] = replacement;
    }

    private sealed class ConnectedTraceSession(SessionManager sessionManager, NamedPipeServerStream server, Task serverTask)
        : IDisposable
    {
        public SessionManager SessionManager { get; } = sessionManager;

        public void Dispose()
        {
            try
            {
                SessionManager.Dispose();
                server.Dispose();
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
