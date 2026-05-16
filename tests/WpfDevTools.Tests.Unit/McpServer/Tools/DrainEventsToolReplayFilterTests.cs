using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.Tools;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public sealed partial class DrainEventsToolReplayTests
{
    [Fact]
    public async Task Execute_WhenReplayMatchesExceedMaxEvents_ShouldKeepUnreturnedReplayEventsForNextDrain()
    {
        const int processId = 43106;
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
                pendingEventCount = 2,
                droppedEventCount = 0,
                pendingEvents = new object[]
                {
                    new
                    {
                        eventType = "DpChange",
                        elementId = "Replay_Max_1",
                        propertyName = "Width",
                        newValue = 101,
                        timestampUtc = DateTimeOffset.UtcNow.AddSeconds(-2)
                    },
                    new
                    {
                        eventType = "DpChange",
                        elementId = "Replay_Max_2",
                        propertyName = "Height",
                        newValue = 202,
                        timestampUtc = DateTimeOffset.UtcNow.AddSeconds(-1)
                    }
                }
            }),
            JsonSerializer.Serialize(new
            {
                success = true,
                pendingEventCount = 0,
                droppedEventCount = 0
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
        firstDrain.GetProperty("pendingEvents")[0].GetProperty("elementId").GetString().Should().Be("Replay_Max_1");
        connected.SessionManager.TryPeekPendingEventReplay(processId, out var replayAfterFirstDrain).Should().BeTrue();
        replayAfterFirstDrain.GetProperty("pendingEventCount").GetInt32().Should().Be(1);
        replayAfterFirstDrain.GetProperty("pendingEvents")[0].GetProperty("elementId").GetString().Should().Be("Replay_Max_2");

        var secondDrain = JsonSerializer.SerializeToElement(await drainTool.ExecuteAsync(
            ToJsonElement(new { processId }),
            CancellationToken.None));

        secondDrain.GetProperty("pendingEventCount").GetInt32().Should().Be(1);
        secondDrain.GetProperty("pendingEvents")[0].GetProperty("elementId").GetString().Should().Be("Replay_Max_2");
        connected.SessionManager.TryPeekPendingEventReplay(processId, out _).Should().BeFalse();
        connected.RequestMethods.Should().Equal("get_binding_errors", "drain_events", "drain_events", "drain_events");
    }

    [Fact]
    public async Task Execute_WhenReplayFilterByEventTypesMatchesSubset_ShouldKeepNonMatchingReplayEvents()
    {
        const int processId = 43107;
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
                pendingEventCount = 2,
                droppedEventCount = 0,
                pendingEvents = new object[]
                {
                    new
                    {
                        eventType = "DpChange",
                        elementId = "Replay_Filter_Type_1",
                        propertyName = "Width",
                        newValue = 303,
                        timestampUtc = DateTimeOffset.UtcNow.AddSeconds(-2)
                    },
                    new
                    {
                        eventType = "BindingError",
                        elementId = "Replay_Filter_Type_2",
                        propertyName = "Text",
                        message = "binding failed",
                        timestampUtc = DateTimeOffset.UtcNow.AddSeconds(-1)
                    }
                }
            }),
            JsonSerializer.Serialize(new
            {
                success = true,
                pendingEventCount = 0,
                droppedEventCount = 0
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

        var filteredDrain = JsonSerializer.SerializeToElement(await drainTool.ExecuteAsync(
            ToJsonElement(new { processId, eventTypes = new[] { "DpChange" } }),
            CancellationToken.None));

        filteredDrain.GetProperty("pendingEventCount").GetInt32().Should().Be(1);
        filteredDrain.GetProperty("pendingEvents")[0].GetProperty("eventType").GetString().Should().Be("DpChange");
        connected.SessionManager.TryPeekPendingEventReplay(processId, out var replayAfterFilteredDrain).Should().BeTrue();
        replayAfterFilteredDrain.GetProperty("pendingEventCount").GetInt32().Should().Be(1);
        replayAfterFilteredDrain.GetProperty("pendingEvents")[0].GetProperty("eventType").GetString().Should().Be("BindingError");

        var laterDrain = JsonSerializer.SerializeToElement(await drainTool.ExecuteAsync(
            ToJsonElement(new { processId, eventTypes = new[] { "BindingError" } }),
            CancellationToken.None));

        laterDrain.GetProperty("pendingEventCount").GetInt32().Should().Be(1);
        laterDrain.GetProperty("pendingEvents")[0].GetProperty("eventType").GetString().Should().Be("BindingError");
        connected.SessionManager.TryPeekPendingEventReplay(processId, out _).Should().BeFalse();
        connected.RequestMethods.Should().Equal("get_binding_errors", "drain_events", "drain_events", "drain_events");
    }

    [Fact]
    public async Task Execute_WhenReplayFilterByElementIdMatchesSubset_ShouldKeepNonMatchingReplayEvents()
    {
        const int processId = 43108;
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
                pendingEventCount = 2,
                droppedEventCount = 0,
                pendingEvents = new object[]
                {
                    new
                    {
                        eventType = "DpChange",
                        elementId = "Replay_Element_Target",
                        propertyName = "Width",
                        newValue = 404,
                        timestampUtc = DateTimeOffset.UtcNow.AddSeconds(-2)
                    },
                    new
                    {
                        eventType = "DpChange",
                        elementId = "Replay_Element_Other",
                        propertyName = "Height",
                        newValue = 505,
                        timestampUtc = DateTimeOffset.UtcNow.AddSeconds(-1)
                    }
                }
            }),
            JsonSerializer.Serialize(new
            {
                success = true,
                pendingEventCount = 0,
                droppedEventCount = 0
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

        var filteredDrain = JsonSerializer.SerializeToElement(await drainTool.ExecuteAsync(
            ToJsonElement(new { processId, elementId = "Replay_Element_Target" }),
            CancellationToken.None));

        filteredDrain.GetProperty("pendingEventCount").GetInt32().Should().Be(1);
        filteredDrain.GetProperty("pendingEvents")[0].GetProperty("elementId").GetString().Should().Be("Replay_Element_Target");
        connected.SessionManager.TryPeekPendingEventReplay(processId, out var replayAfterFilteredDrain).Should().BeTrue();
        replayAfterFilteredDrain.GetProperty("pendingEventCount").GetInt32().Should().Be(1);
        replayAfterFilteredDrain.GetProperty("pendingEvents")[0].GetProperty("elementId").GetString().Should().Be("Replay_Element_Other");

        var laterDrain = JsonSerializer.SerializeToElement(await drainTool.ExecuteAsync(
            ToJsonElement(new { processId, elementId = "Replay_Element_Other" }),
            CancellationToken.None));

        laterDrain.GetProperty("pendingEventCount").GetInt32().Should().Be(1);
        laterDrain.GetProperty("pendingEvents")[0].GetProperty("elementId").GetString().Should().Be("Replay_Element_Other");
        connected.SessionManager.TryPeekPendingEventReplay(processId, out _).Should().BeFalse();
        connected.RequestMethods.Should().Equal("get_binding_errors", "drain_events", "drain_events", "drain_events");
    }

    [Fact]
    public async Task Execute_WhenReplayFilterBySinceTimestampMatchesSubset_ShouldKeepOlderReplayEvents()
    {
        const int processId = 43109;
        var olderTimestamp = DateTimeOffset.UtcNow.AddSeconds(-10);
        var newerTimestamp = DateTimeOffset.UtcNow.AddSeconds(-1);

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
                pendingEventCount = 2,
                droppedEventCount = 0,
                pendingEvents = new object[]
                {
                    new
                    {
                        eventType = "DpChange",
                        elementId = "Replay_Time_Older",
                        propertyName = "Width",
                        newValue = 606,
                        timestampUtc = olderTimestamp
                    },
                    new
                    {
                        eventType = "DpChange",
                        elementId = "Replay_Time_Newer",
                        propertyName = "Height",
                        newValue = 707,
                        timestampUtc = newerTimestamp
                    }
                }
            }),
            JsonSerializer.Serialize(new
            {
                success = true,
                pendingEventCount = 0,
                droppedEventCount = 0
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

        var filteredDrain = JsonSerializer.SerializeToElement(await drainTool.ExecuteAsync(
            ToJsonElement(new { processId, sinceTimestamp = newerTimestamp.ToString("O") }),
            CancellationToken.None));

        filteredDrain.GetProperty("pendingEventCount").GetInt32().Should().Be(1);
        filteredDrain.GetProperty("pendingEvents")[0].GetProperty("elementId").GetString().Should().Be("Replay_Time_Newer");
        connected.SessionManager.TryPeekPendingEventReplay(processId, out var replayAfterFilteredDrain).Should().BeTrue();
        replayAfterFilteredDrain.GetProperty("pendingEventCount").GetInt32().Should().Be(1);
        replayAfterFilteredDrain.GetProperty("pendingEvents")[0].GetProperty("elementId").GetString().Should().Be("Replay_Time_Older");

        var laterDrain = JsonSerializer.SerializeToElement(await drainTool.ExecuteAsync(
            ToJsonElement(new { processId }),
            CancellationToken.None));

        laterDrain.GetProperty("pendingEventCount").GetInt32().Should().Be(1);
        laterDrain.GetProperty("pendingEvents")[0].GetProperty("elementId").GetString().Should().Be("Replay_Time_Older");
        connected.SessionManager.TryPeekPendingEventReplay(processId, out _).Should().BeFalse();
        connected.RequestMethods.Should().Equal("get_binding_errors", "drain_events", "drain_events", "drain_events");
    }
}
