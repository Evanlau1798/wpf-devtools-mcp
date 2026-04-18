using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Serialization;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public sealed class DrainEventsToolReplayTests
{
    [Fact]
    public async Task Execute_AfterPiggybackConsumption_ShouldReplayLatestPendingEventsOnce()
    {
        const int processId = 43103;
        using var connected = await ConnectedReplaySession.CreateAsync(
            processId,
            JsonSerializer.Serialize(new
            {
                success = true,
                errorCount = 0,
                errors = Array.Empty<object>()
            }),
            JsonSerializer.Serialize(new
            {
                success = true,
                pendingEventCount = 1,
                droppedEventCount = 0,
                pendingEvents = new[]
                {
                    new
                    {
                        eventType = "DpChange",
                        elementId = "Button_1",
                        propertyName = "Width",
                        newValue = 222,
                        timestampUtc = DateTimeOffset.UtcNow
                    }
                }
            }),
            JsonSerializer.Serialize(new
            {
                success = true,
                pendingEventCount = 0,
                droppedEventCount = 0
            }));

        var triggerTool = new GetBindingErrorsTool(connected.SessionManager);
        var drainTool = new DrainEventsTool(connected.SessionManager);

        var triggerResult = JsonSerializer.SerializeToElement(await triggerTool.ExecuteAsync(
            ToJsonElement(new { processId }),
            CancellationToken.None));
        var replayResult = JsonSerializer.SerializeToElement(await drainTool.ExecuteAsync(
            ToJsonElement(new { processId }),
            CancellationToken.None));

        triggerResult.GetProperty("pendingEventCount").GetInt32().Should().Be(1);
        replayResult.GetProperty("success").GetBoolean().Should().BeTrue();
        replayResult.GetProperty("pendingEventCount").GetInt32().Should().Be(1);
        replayResult.GetProperty("pendingEvents")[0].GetProperty("eventType").GetString().Should().Be("DpChange");
        replayResult.GetProperty("pendingEvents")[0].GetProperty("elementId").GetString().Should().Be("Button_1");

        connected.RequestMethods.Should().Equal("get_binding_errors", "drain_events", "drain_events");
    }

    private sealed class ConnectedReplaySession(
        SessionManager sessionManager,
        NamedPipeServerStream server,
        Task serverTask,
        List<string> requestMethods) : IDisposable
    {
        public SessionManager SessionManager { get; } = sessionManager;
        public IReadOnlyList<string> RequestMethods { get; } = requestMethods;

        public static async Task<ConnectedReplaySession> CreateAsync(int processId, params string[] responses)
        {
            var pipeName = $"WpfDevTools_Test_{Guid.NewGuid():N}";
            var server = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
            var requestMethods = new List<string>();
            var responseQueue = new Queue<string>(responses);

            var serverTask = Task.Run(async () =>
            {
                await server.WaitForConnectionAsync();
                try
                {
                    while (server.IsConnected && responseQueue.Count > 0)
                    {
                        var requestJson = await MessageFraming.ReadMessageAsync(server, CancellationToken.None);
                        var request = JsonSerializer.Deserialize<InspectorRequest>(requestJson)!;
                        requestMethods.Add(request.Method);

                        var response = new InspectorResponse
                        {
                            Id = request.Id,
                            CorrelationId = request.CorrelationId,
                            Result = JsonSerializer.Deserialize<JsonElement>(responseQueue.Dequeue())
                        };

                        await MessageFraming.WriteMessageAsync(
                            server,
                            JsonSerializer.Serialize(response),
                            CancellationToken.None);
                    }
                }
                catch (EndOfStreamException)
                {
                }
                catch (IOException)
                {
                }
                catch (ObjectDisposedException)
                {
                }
            });

            var sessionManager = new SessionManager();
            DisableSessionManagerCleanupTimer(sessionManager);
            sessionManager.AddSession(processId);
            var client = new NamedPipeClient(processId, pipeName);
            (await client.ConnectAsync(TimeSpan.FromSeconds(5), maxRetries: 1)).Should().BeTrue();
            ReplaceSessionManagerPipeClient(sessionManager, processId, client);

            return new ConnectedReplaySession(sessionManager, server, serverTask, requestMethods);
        }

        public void Dispose()
        {
            try
            {
                SessionManager.Dispose();
                server.Dispose();
                try
                {
                    serverTask.GetAwaiter().GetResult();
                }
                catch (EndOfStreamException)
                {
                }
                catch (IOException)
                {
                }
                catch (ObjectDisposedException)
                {
                }
            }
            finally
            {
                SessionManager.Dispose();
                server.Dispose();
            }
        }

    }
}
