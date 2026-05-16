using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.Tools;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public sealed partial class DrainEventsToolReplayTests
{
    [Fact]
    public async Task Execute_WhenReplayConsumesMaxEvents_ShouldRetainOverflowLiveEventsForNextDrain()
    {
        const int processId = 43110;
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
                pendingEvents = new object[]
                {
                    new
                    {
                        eventType = "DpChange",
                        elementId = "Replay_First",
                        propertyName = "Width",
                        newValue = 808,
                        timestampUtc = DateTimeOffset.UtcNow.AddSeconds(-3)
                    }
                }
            }),
            JsonSerializer.Serialize(new
            {
                success = true,
                pendingEventCount = 2,
                droppedEventCount = 0,
                pendingEvents = new object[]
                {
                    new
                    {
                        eventType = "DpChange",
                        elementId = "Live_Overflow_1",
                        propertyName = "Height",
                        newValue = 909,
                        timestampUtc = DateTimeOffset.UtcNow.AddSeconds(-2)
                    },
                    new
                    {
                        eventType = "DpChange",
                        elementId = "Live_Overflow_2",
                        propertyName = "Opacity",
                        newValue = 1,
                        timestampUtc = DateTimeOffset.UtcNow.AddSeconds(-1)
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

        _ = await triggerTool.ExecuteAsync(ToJsonElement(new { processId }), CancellationToken.None);

        var firstDrain = JsonSerializer.SerializeToElement(await drainTool.ExecuteAsync(
            ToJsonElement(new { processId, maxEvents = 1 }),
            CancellationToken.None));

        firstDrain.GetProperty("pendingEventCount").GetInt32().Should().Be(1);
        firstDrain.GetProperty("pendingEvents")[0].GetProperty("elementId").GetString().Should().Be("Replay_First");
        connected.SessionManager.TryPeekPendingEventReplay(processId, out var replayAfterFirstDrain).Should().BeTrue();
        replayAfterFirstDrain.GetProperty("pendingEventCount").GetInt32().Should().Be(2);
        replayAfterFirstDrain.GetProperty("pendingEvents")[0].GetProperty("elementId").GetString().Should().Be("Live_Overflow_1");
        replayAfterFirstDrain.GetProperty("pendingEvents")[1].GetProperty("elementId").GetString().Should().Be("Live_Overflow_2");

        var secondDrain = JsonSerializer.SerializeToElement(await drainTool.ExecuteAsync(
            ToJsonElement(new { processId }),
            CancellationToken.None));

        secondDrain.GetProperty("pendingEventCount").GetInt32().Should().Be(2);
        secondDrain.GetProperty("pendingEvents")[0].GetProperty("elementId").GetString().Should().Be("Live_Overflow_1");
        secondDrain.GetProperty("pendingEvents")[1].GetProperty("elementId").GetString().Should().Be("Live_Overflow_2");
        connected.SessionManager.TryPeekPendingEventReplay(processId, out _).Should().BeFalse();
        connected.RequestMethods.Should().Equal("get_binding_errors", "drain_events", "drain_events", "drain_events");
    }

    [Fact]
    public async Task Execute_WhenReplayExists_ShouldRetainLiveOverflowBeyondInspectorDefaultDrainWindow()
    {
        const int processId = 43111;
        var liveOverflowEvents = Enumerable.Range(1, 60)
            .Select(index => new
            {
                eventType = "DpChange",
                elementId = $"Live_Overflow_{index}",
                propertyName = "Width",
                newValue = index,
                timestampUtc = DateTimeOffset.UtcNow.AddSeconds(index)
            })
            .ToArray();

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
                pendingEvents = new object[]
                {
                    new
                    {
                        eventType = "DpChange",
                        elementId = "Replay_Default_Window_First",
                        propertyName = "Height",
                        newValue = 1001,
                        timestampUtc = DateTimeOffset.UtcNow.AddSeconds(-1)
                    }
                }
            }),
            JsonSerializer.Serialize(new
            {
                success = true,
                pendingEventCount = liveOverflowEvents.Length,
                droppedEventCount = 0,
                pendingEvents = liveOverflowEvents
            }),
            JsonSerializer.Serialize(new
            {
                success = true,
                pendingEventCount = 0,
                droppedEventCount = 0
            }));

        var triggerTool = new GetBindingErrorsTool(connected.SessionManager);
        var drainTool = new DrainEventsTool(connected.SessionManager);

        _ = await triggerTool.ExecuteAsync(ToJsonElement(new { processId }), CancellationToken.None);

        var firstDrain = JsonSerializer.SerializeToElement(await drainTool.ExecuteAsync(
            ToJsonElement(new { processId, maxEvents = 1 }),
            CancellationToken.None));

        firstDrain.GetProperty("pendingEventCount").GetInt32().Should().Be(1);
        firstDrain.GetProperty("pendingEvents")[0].GetProperty("elementId").GetString().Should().Be("Replay_Default_Window_First");
        connected.Requests[2].Method.Should().Be("drain_events");
        connected.Requests[2].Params.Should().NotBeNull();
        connected.Requests[2].Params!.Value.GetProperty("maxEvents").GetInt32().Should().Be(int.MaxValue);
        connected.SessionManager.TryPeekPendingEventReplay(processId, out var replayAfterFirstDrain).Should().BeTrue();
        replayAfterFirstDrain.GetProperty("pendingEventCount").GetInt32().Should().Be(60);
        replayAfterFirstDrain.GetProperty("pendingEvents")[0].GetProperty("elementId").GetString().Should().Be("Live_Overflow_1");
        replayAfterFirstDrain.GetProperty("pendingEvents")[59].GetProperty("elementId").GetString().Should().Be("Live_Overflow_60");

        var secondDrain = JsonSerializer.SerializeToElement(await drainTool.ExecuteAsync(
            ToJsonElement(new { processId }),
            CancellationToken.None));

        secondDrain.GetProperty("pendingEventCount").GetInt32().Should().Be(60);
        secondDrain.GetProperty("pendingEvents")[0].GetProperty("elementId").GetString().Should().Be("Live_Overflow_1");
        secondDrain.GetProperty("pendingEvents")[59].GetProperty("elementId").GetString().Should().Be("Live_Overflow_60");
        connected.SessionManager.TryPeekPendingEventReplay(processId, out _).Should().BeFalse();
        connected.RequestMethods.Should().Equal("get_binding_errors", "drain_events", "drain_events", "drain_events");
    }
}
