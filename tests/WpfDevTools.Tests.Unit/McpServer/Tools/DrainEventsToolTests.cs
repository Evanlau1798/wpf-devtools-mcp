using System.IO.Pipes;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Serialization;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public sealed class DrainEventsToolTests
{
    [Fact]
    public async Task Execute_WithoutConnection_ShouldReturnError()
    {
        var tool = new DrainEventsTool(new SessionManager());

        var result = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new { processId = 43101 }),
            CancellationToken.None);

        var payload = JsonSerializer.SerializeToElement(result);
        payload.GetProperty("success").GetBoolean().Should().BeFalse();
        payload.GetProperty("error").GetString().Should().Contain("not connected");
    }

    [Fact]
    public async Task Execute_WithFilters_ShouldForwardDrainRequestToInspector()
    {
        const int processId = 43102;
        var responseJson =
            """{"success":true,"pendingEventCount":1,"droppedEventCount":0,"pendingEvents":[{"eventType":"DpChange","elementId":"Button_1","propertyName":"Width"}]}""";
        using var connected = await ConnectedDrainSession.CreateAsync(processId, responseJson);
        var tool = new DrainEventsTool(connected.SessionManager);
        var sinceTimestamp = DateTimeOffset.UtcNow.ToString("O");

        var result = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new
            {
                processId,
                maxEvents = 5,
                eventTypes = new[] { "DpChange" },
                elementId = "Button_1",
                sinceTimestamp
            }),
            CancellationToken.None);

        var request = await connected.RequestTask;
        request.Method.Should().Be("drain_events");
        request.Params.Should().NotBeNull();
        request.Params!.Value.GetProperty("maxEvents").GetInt32().Should().Be(5);
        request.Params.Value.GetProperty("eventTypes")[0].GetString().Should().Be("DpChange");
        request.Params.Value.GetProperty("elementId").GetString().Should().Be("Button_1");
        request.Params.Value.GetProperty("sinceTimestamp").GetString().Should().Be(sinceTimestamp);

        var payload = JsonSerializer.SerializeToElement(result);
        payload.GetProperty("success").GetBoolean().Should().BeTrue();
        payload.GetProperty("pendingEventCount").GetInt32().Should().Be(1);
        payload.GetProperty("pendingEvents")[0].GetProperty("eventType").GetString().Should().Be("DpChange");
    }

    private sealed class ConnectedDrainSession(
        SessionManager sessionManager,
        NamedPipeServerStream server,
        Task serverTask,
        Task<InspectorRequest> requestTask) : IDisposable
    {
        public SessionManager SessionManager { get; } = sessionManager;
        public Task<InspectorRequest> RequestTask { get; } = requestTask;

        public static async Task<ConnectedDrainSession> CreateAsync(int processId, string responseJson)
        {
            var pipeName = $"WpfDevTools_Test_{Guid.NewGuid():N}";
            var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            var requestSource = new TaskCompletionSource<InspectorRequest>(TaskCreationOptions.RunContinuationsAsynchronously);

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
                        requestSource.TrySetResult(request!);

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

            return new ConnectedDrainSession(sessionManager, server, serverTask, requestSource.Task);
        }

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
    }
}
