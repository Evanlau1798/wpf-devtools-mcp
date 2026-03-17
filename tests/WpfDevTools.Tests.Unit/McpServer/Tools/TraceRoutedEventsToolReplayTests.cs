using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Navigation;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Serialization;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public sealed class TraceRoutedEventsToolReplayTests
{
    [Fact]
    public async Task Execute_GetMode_ShouldMergeMatchingPendingEventReplay_WithoutConsumingDrainReplay()
    {
        const int processId = 43121;
        using var connected = await ConnectedTraceReplaySession.CreateAsync(
            processId,
            """{"success":true,"mode":"get","isTracing":true,"eventCount":0,"events":[],"handlerInvocationCount":0}""",
            """{"success":true,"pendingEventCount":0,"droppedEventCount":0}""",
            """{"success":true,"pendingEventCount":0,"droppedEventCount":0}""");

        connected.SessionManager.SetActiveTraceState(
            processId,
            new ActiveTraceNavigationState(
                "Click",
                "Button_46",
                DateTimeOffset.UtcNow.AddMilliseconds(-200),
                TimeSpan.FromMilliseconds(2500)));
        connected.SessionManager.SavePendingEventReplay(
            processId,
            JsonSerializer.SerializeToElement(new
            {
                success = true,
                pendingEventCount = 1,
                droppedEventCount = 0,
                pendingEvents = new[]
                {
                    new
                    {
                        eventType = "RoutedEvent",
                        elementId = "Button_46",
                        eventName = "Click",
                        senderType = "Button",
                        senderName = "EventStormButton",
                        routingStrategy = "Bubble",
                        handled = false,
                        originalSourceType = "Button",
                        timestampUtc = DateTimeOffset.UtcNow
                    }
                }
            }));

        var traceTool = new TraceRoutedEventsTool(connected.SessionManager);
        var drainTool = new DrainEventsTool(connected.SessionManager);

        var traceResult = JsonSerializer.SerializeToElement(await traceTool.ExecuteAsync(
            ToJsonElement(new { processId, mode = "get" }),
            CancellationToken.None));
        var drainResult = JsonSerializer.SerializeToElement(await drainTool.ExecuteAsync(
            ToJsonElement(new { processId, eventTypes = new[] { "RoutedEvent" } }),
            CancellationToken.None));

        traceResult.GetProperty("success").GetBoolean().Should().BeTrue(traceResult.GetRawText());
        traceResult.GetProperty("eventCount").GetInt32().Should().Be(1, traceResult.GetRawText());
        traceResult.GetProperty("events")[0].GetProperty("eventName").GetString().Should().Be("Click");
        traceResult.GetProperty("events")[0].GetProperty("sender").GetString().Should().Be("Button");
        traceResult.GetProperty("handlerInvocationCount").GetInt32().Should().Be(0);

        drainResult.GetProperty("success").GetBoolean().Should().BeTrue(drainResult.GetRawText());
        drainResult.GetProperty("pendingEventCount").GetInt32().Should().Be(1, drainResult.GetRawText());
        drainResult.GetProperty("pendingEvents")[0].GetProperty("eventName").GetString().Should().Be("Click");
    }

    [Fact]
    public async Task Execute_GetMode_ShouldIgnoreNonMatchingPendingEventReplay()
    {
        const int processId = 43122;
        using var connected = await ConnectedTraceReplaySession.CreateAsync(
            processId,
            """{"success":true,"mode":"get","isTracing":true,"eventCount":0,"events":[],"handlerInvocationCount":0,"diagnostics":{"reasonCode":"captureWindowTooShort"}}""",
            """{"success":true,"pendingEventCount":0,"droppedEventCount":0}""");

        connected.SessionManager.SetActiveTraceState(
            processId,
            new ActiveTraceNavigationState(
                "Click",
                "Button_46",
                DateTimeOffset.UtcNow.AddMilliseconds(-200),
                TimeSpan.FromMilliseconds(2500)));
        connected.SessionManager.SavePendingEventReplay(
            processId,
            JsonSerializer.SerializeToElement(new
            {
                success = true,
                pendingEventCount = 1,
                droppedEventCount = 0,
                pendingEvents = new[]
                {
                    new
                    {
                        eventType = "RoutedEvent",
                        elementId = "Border_47",
                        eventName = "MouseDown",
                        senderType = "Border",
                        senderName = "RoutedProbeBorder",
                        routingStrategy = "Bubble",
                        handled = false,
                        originalSourceType = "Border",
                        timestampUtc = DateTimeOffset.UtcNow
                    }
                }
            }));

        var traceTool = new TraceRoutedEventsTool(connected.SessionManager);

        var traceResult = JsonSerializer.SerializeToElement(await traceTool.ExecuteAsync(
            ToJsonElement(new { processId, mode = "get" }),
            CancellationToken.None));

        traceResult.GetProperty("success").GetBoolean().Should().BeTrue(traceResult.GetRawText());
        traceResult.GetProperty("eventCount").GetInt32().Should().Be(0, traceResult.GetRawText());
        traceResult.GetProperty("diagnostics").GetProperty("reasonCode").GetString().Should().Be("captureWindowTooShort");
    }

    [Fact]
    public async Task Execute_GetMode_AfterFireRoutedEventMouseDown_ShouldMergeReplayFromRealToolSequence()
    {
        const int processId = 43123;
        using var connected = await ConnectedTraceReplaySession.CreateAsync(
            processId,
            """{"success":true,"mode":"start","eventName":"MouseDown","isTracing":true,"effectiveDuration":2500}""",
            """{"success":true,"pendingEventCount":0,"droppedEventCount":0}""",
            """{"success":true,"message":"Event 'MouseDown' fired successfully","eventName":"MouseDown"}""",
            JsonSerializer.Serialize(new
            {
                success = true,
                pendingEventCount = 1,
                droppedEventCount = 0,
                pendingEvents = new[]
                {
                    new
                    {
                        eventType = "RoutedEvent",
                        elementId = "Border_47",
                        eventName = "MouseDown",
                        senderType = "Border",
                        senderName = "RoutedProbeBorder",
                        routingStrategy = "Bubble",
                        handled = false,
                        originalSourceType = "Border",
                        timestampUtc = DateTimeOffset.UtcNow
                    }
                }
            }),
            """{"success":true,"mode":"get","isTracing":true,"eventCount":0,"events":[],"handlerInvocationCount":0}""",
            """{"success":true,"pendingEventCount":0,"droppedEventCount":0}""",
            """{"success":true,"pendingEventCount":0,"droppedEventCount":0}""");

        var traceTool = new TraceRoutedEventsTool(connected.SessionManager);
        var fireTool = new FireRoutedEventTool(connected.SessionManager);
        var drainTool = new DrainEventsTool(connected.SessionManager);

        var startResult = JsonSerializer.SerializeToElement(await traceTool.ExecuteAsync(
            ToJsonElement(new
            {
                processId,
                elementId = "Border_47",
                eventName = "MouseDown",
                mode = "start",
                duration = 2500,
                allowShortStartDuration = true
            }),
            CancellationToken.None));
        startResult.GetProperty("success").GetBoolean().Should().BeTrue(startResult.GetRawText());

        var fireResult = JsonSerializer.SerializeToElement(await fireTool.ExecuteAsync(
            ToJsonElement(new
            {
                processId,
                elementId = "Border_47",
                eventName = "MouseDown"
            }),
            CancellationToken.None));
        fireResult.GetProperty("success").GetBoolean().Should().BeTrue(fireResult.GetRawText());
        fireResult.GetProperty("pendingEventCount").GetInt32().Should().Be(1, fireResult.GetRawText());

        connected.SessionManager.TryGetNavigationState(processId, out var state).Should().BeTrue();
        state!.ActiveTrace.Should().NotBeNull();
        state.ActiveTrace!.EventName.Should().Be("MouseDown");
        state.ActiveTrace.ElementId.Should().Be("Border_47");

        connected.SessionManager.TryPeekPendingEventReplay(processId, out var replayPayload).Should().BeTrue();
        replayPayload.GetProperty("pendingEventCount").GetInt32().Should().Be(1);
        replayPayload.GetProperty("pendingEvents")[0].GetProperty("eventName").GetString().Should().Be("MouseDown");
        replayPayload.GetProperty("pendingEvents")[0].GetProperty("elementId").GetString().Should().Be("Border_47");

        var traceResult = JsonSerializer.SerializeToElement(await traceTool.ExecuteAsync(
            ToJsonElement(new { processId, mode = "get" }),
            CancellationToken.None));
        var drainResult = JsonSerializer.SerializeToElement(await drainTool.ExecuteAsync(
            ToJsonElement(new { processId, eventTypes = new[] { "RoutedEvent" } }),
            CancellationToken.None));

        traceResult.GetProperty("success").GetBoolean().Should().BeTrue(traceResult.GetRawText());
        traceResult.GetProperty("eventCount").GetInt32().Should().Be(1, traceResult.GetRawText());
        traceResult.GetProperty("events")[0].GetProperty("eventName").GetString().Should().Be("MouseDown");
        traceResult.GetProperty("events")[0].GetProperty("sender").GetString().Should().Be("Border");

        drainResult.GetProperty("success").GetBoolean().Should().BeTrue(drainResult.GetRawText());
        drainResult.GetProperty("pendingEventCount").GetInt32().Should().Be(1, drainResult.GetRawText());
        drainResult.GetProperty("pendingEvents")[0].GetProperty("eventName").GetString().Should().Be("MouseDown");
    }

    private sealed class ConnectedTraceReplaySession(
        SessionManager sessionManager,
        NamedPipeServerStream server,
        Task serverTask) : IDisposable
    {
        public SessionManager SessionManager { get; } = sessionManager;

        public static async Task<ConnectedTraceReplaySession> CreateAsync(int processId, params string[] responses)
        {
            var pipeName = $"WpfDevTools_Test_{Guid.NewGuid():N}";
            var server = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
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
                catch (ObjectDisposedException)
                {
                }
            });

            var sessionManager = new SessionManager();
            sessionManager.AddSession(processId);
            var client = new NamedPipeClient(processId, pipeName);
            (await client.ConnectAsync(TimeSpan.FromSeconds(5), maxRetries: 1)).Should().BeTrue();
            ReplacePipeClient(sessionManager, processId, client);

            return new ConnectedTraceReplaySession(sessionManager, server, serverTask);
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
