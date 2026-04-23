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

    [Fact]
    public async Task Execute_AfterPiggybackCleanupFailure_ShouldReplayCleanupDiagnostics()
    {
        const int processId = 43104;
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
                pendingEventCount = 0,
                droppedEventCount = 0,
                cleanupIncomplete = true,
                cleanupFailureMessage = "cleanup failed",
                cleanupFailureType = "InvalidOperationException"
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

        triggerResult.GetProperty("cleanupIncomplete").GetBoolean().Should().BeTrue();
        replayResult.GetProperty("success").GetBoolean().Should().BeTrue();
        replayResult.GetProperty("cleanupIncomplete").GetBoolean().Should().BeTrue();
        replayResult.GetProperty("cleanupFailureMessage").GetString().Should().Be("cleanup failed");
        replayResult.GetProperty("cleanupFailureType").GetString().Should().Be("InvalidOperationException");

        connected.RequestMethods.Should().Equal("get_binding_errors", "drain_events", "drain_events");
    }

    [Fact]
    public async Task Execute_WhenLiveDrainFails_ShouldKeepReplayAvailableForNextSuccessfulDrain()
    {
        const int processId = 43105;
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
                        elementId = "Replay_Button",
                        propertyName = "Width",
                        newValue = 123,
                        timestampUtc = DateTimeOffset.UtcNow
                    }
                }
            }),
            JsonSerializer.Serialize(new
            {
                success = false,
                error = "live drain failed",
                errorCode = "Timeout"
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

        var failedDrain = JsonSerializer.SerializeToElement(await drainTool.ExecuteAsync(
            ToJsonElement(new { processId }),
            CancellationToken.None));
        failedDrain.GetProperty("success").GetBoolean().Should().BeFalse();
        failedDrain.GetProperty("errorData").GetProperty("replayPreserved").GetBoolean().Should().BeTrue();
        failedDrain.GetProperty("errorData").GetProperty("bufferedReplayEventCount").GetInt32().Should().Be(1);
        failedDrain.GetProperty("recovery").GetProperty("suggestedAction").GetString().Should().Contain("Retry drain_events");
        connected.SessionManager.TryPeekPendingEventReplay(processId, out var replayAfterFailure).Should().BeTrue();
        replayAfterFailure.GetProperty("pendingEventCount").GetInt32().Should().Be(1);

        var successfulDrain = JsonSerializer.SerializeToElement(await drainTool.ExecuteAsync(
            ToJsonElement(new { processId }),
            CancellationToken.None));

        successfulDrain.GetProperty("success").GetBoolean().Should().BeTrue();
        successfulDrain.GetProperty("pendingEventCount").GetInt32().Should().Be(1);
        successfulDrain.GetProperty("pendingEvents")[0].GetProperty("elementId").GetString().Should().Be("Replay_Button");
        connected.SessionManager.TryPeekPendingEventReplay(processId, out _).Should().BeFalse();
        connected.RequestMethods.Should().Equal("get_binding_errors", "drain_events", "drain_events", "drain_events");
    }

    [Fact]
    public async Task Execute_WhenReplayWasClearedBeforeLiveFailure_ShouldNotAdvertiseReplayPreservedRecovery()
    {
        const int processId = 431051;
        var drainRequestCount = 0;
        using var connected = await ConnectedReplaySession.CreateAsync(
            processId,
            onRequestAsync: (requestIndex, request, sessionManager) =>
            {
                if (!string.Equals(request.Method, "drain_events", StringComparison.Ordinal))
                {
                    return Task.CompletedTask;
                }

                drainRequestCount++;
                if (drainRequestCount == 2)
                {
                    sessionManager.TryTakePendingEventReplay(processId, out _).Should().BeTrue();
                }

                return Task.CompletedTask;
            },
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
                        elementId = "Replay_Button_Cleared",
                        propertyName = "Width",
                        newValue = 321,
                        timestampUtc = DateTimeOffset.UtcNow
                    }
                }
            }),
            JsonSerializer.Serialize(new
            {
                success = false,
                error = "live drain failed",
                errorCode = "Timeout"
            }));

        var triggerTool = new GetBindingErrorsTool(connected.SessionManager);
        var drainTool = new DrainEventsTool(connected.SessionManager);

        _ = await triggerTool.ExecuteAsync(ToJsonElement(new { processId }), CancellationToken.None);

        var failedDrain = JsonSerializer.SerializeToElement(await drainTool.ExecuteAsync(
            ToJsonElement(new { processId }),
            CancellationToken.None));

        failedDrain.GetProperty("success").GetBoolean().Should().BeFalse();
        failedDrain.TryGetProperty("errorData", out _).Should().BeFalse();
        failedDrain.TryGetProperty("recovery", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Execute_AfterMultiplePiggybackCycles_ShouldPreservePriorReplayWhenNewLiveEventsArrive()
    {
        const int processId = 431052;
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
                        elementId = "Replay_Prior",
                        propertyName = "Width",
                        newValue = 111,
                        timestampUtc = DateTimeOffset.UtcNow.AddSeconds(-2)
                    }
                }
            }),
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
                        elementId = "Replay_Live",
                        propertyName = "Height",
                        newValue = 222,
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

        var firstResult = JsonSerializer.SerializeToElement(await triggerTool.ExecuteAsync(
            ToJsonElement(new { processId }),
            CancellationToken.None));
        var secondResult = JsonSerializer.SerializeToElement(await triggerTool.ExecuteAsync(
            ToJsonElement(new { processId }),
            CancellationToken.None));
        var replayResult = JsonSerializer.SerializeToElement(await drainTool.ExecuteAsync(
            ToJsonElement(new { processId }),
            CancellationToken.None));

        firstResult.GetProperty("pendingEventCount").GetInt32().Should().Be(1);
        secondResult.GetProperty("pendingEventCount").GetInt32().Should().Be(2);
        secondResult.GetProperty("pendingEvents").EnumerateArray().Select(item => item.GetProperty("elementId").GetString())
            .Should().Equal("Replay_Prior", "Replay_Live");
        secondResult.GetProperty("pendingEventsMayIncludePriorContext").GetBoolean().Should().BeTrue();

        replayResult.GetProperty("success").GetBoolean().Should().BeTrue();
        replayResult.GetProperty("pendingEventCount").GetInt32().Should().Be(2);
        replayResult.GetProperty("pendingEvents").EnumerateArray().Select(item => item.GetProperty("elementId").GetString())
            .Should().Equal("Replay_Prior", "Replay_Live");
        connected.SessionManager.TryPeekPendingEventReplay(processId, out _).Should().BeFalse();
    }

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

    private sealed class ConnectedReplaySession(
        SessionManager sessionManager,
        NamedPipeServerStream server,
        Task serverTask,
        List<string> requestMethods,
        List<RecordedInspectorRequest> requests) : IDisposable
    {
        public SessionManager SessionManager { get; } = sessionManager;
        public IReadOnlyList<string> RequestMethods { get; } = requestMethods;
        public IReadOnlyList<RecordedInspectorRequest> Requests { get; } = requests;

        public static Task<ConnectedReplaySession> CreateAsync(int processId, params string[] responses)
            => CreateAsync(processId, onRequestAsync: null, responses);

        public static async Task<ConnectedReplaySession> CreateAsync(
            int processId,
            Func<int, RecordedInspectorRequest, SessionManager, Task>? onRequestAsync,
            params string[] responses)
        {
            var sessionManager = new SessionManager();
            DisableSessionManagerCleanupTimer(sessionManager);
            sessionManager.AddSession(processId);

            var pipeName = $"WpfDevTools_Test_{Guid.NewGuid():N}";
            var server = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
            var requestMethods = new List<string>();
            var requests = new List<RecordedInspectorRequest>();
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
                        var recordedRequest = new RecordedInspectorRequest(request.Method, request.Params?.Clone());
                        requests.Add(recordedRequest);

                        if (onRequestAsync is not null)
                        {
                            await onRequestAsync(requestMethods.Count, recordedRequest, sessionManager);
                        }

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

            var client = new NamedPipeClient(processId, pipeName);
            (await client.ConnectAsync(TimeSpan.FromSeconds(5), maxRetries: 1)).Should().BeTrue();
            ReplaceSessionManagerPipeClient(sessionManager, processId, client);

            return new ConnectedReplaySession(sessionManager, server, serverTask, requestMethods, requests);
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

    private sealed record RecordedInspectorRequest(string Method, JsonElement? Params);
}
