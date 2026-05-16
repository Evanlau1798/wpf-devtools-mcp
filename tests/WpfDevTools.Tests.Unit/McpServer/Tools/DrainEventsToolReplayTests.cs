using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.Tools;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public sealed partial class DrainEventsToolReplayTests
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
}
