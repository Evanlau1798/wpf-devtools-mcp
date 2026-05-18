using System.Text.Json;
using FluentAssertions;
using System.Reflection;
using WpfDevTools.Mcp.Server;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class SessionManagerPendingEventReplayTests
{
    [Fact]
    public void AttachSession_ShouldInitializeSessionGenerationForPendingEventReplay()
    {
        const int processId = 43113;
        using var sessionManager = new SessionManager();
        DisableSessionManagerCleanupTimer(sessionManager);
        using var pipeClient = new NamedPipeClient(processId);

        sessionManager.AttachSession(processId, pipeClient);

        sessionManager.TryGetSessionGeneration(processId, out var sessionGeneration).Should().BeTrue();
        sessionGeneration.Should().BePositive();

        sessionManager.SavePendingEventReplay(
            processId,
            JsonSerializer.SerializeToElement(new
            {
                success = true,
                pendingEventCount = 1,
                droppedEventCount = 0,
                pendingEvents = new object[]
                {
                    new
                    {
                        eventType = "DpChange",
                        elementId = "Attach_Replay_Event",
                        propertyName = "Width",
                        newValue = 2,
                        timestampUtc = DateTimeOffset.UtcNow
                    }
                }
            }));

        sessionManager.TryPeekPendingEventReplay(processId, out var replayPayload).Should().BeTrue();
        replayPayload.GetProperty("pendingEventCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task SavePendingEventReplay_WithStaleSessionGeneration_ShouldNotLeakIntoReconnectedSession()
    {
        const int processId = 43112;
        using var sessionManager = new SessionManager();
        DisableSessionManagerCleanupTimer(sessionManager);
        sessionManager.AddSession(processId);

        using var replayLock = await sessionManager.AcquirePendingEventReplayLockAsync(processId, CancellationToken.None);
        var staleGeneration = replayLock.SessionGeneration;

        sessionManager.RemoveSession(processId);
        sessionManager.AddSession(processId);

        var saved = sessionManager.SavePendingEventReplay(
            processId,
            staleGeneration,
            JsonSerializer.SerializeToElement(new
            {
                success = true,
                pendingEventCount = 1,
                droppedEventCount = 0,
                pendingEvents = new object[]
                {
                    new
                    {
                        eventType = "DpChange",
                        elementId = "Stale_Replay_Event",
                        propertyName = "Width",
                        newValue = 1,
                        timestampUtc = DateTimeOffset.UtcNow
                    }
                }
            }));

        saved.Should().BeFalse("a stale in-flight drain from the previous session generation must not repopulate replay state after reconnect");
        sessionManager.GetPipeClient(processId, staleGeneration).Should().BeNull(
            "a stale in-flight request must not bind to the fresh session's pipe client after same-process reconnect");
        sessionManager.TryPeekPendingEventReplay(processId, out _).Should().BeFalse();
    }

    [Fact]
    public async Task AcquirePendingEventReplayLockAsync_WithoutCurrentSession_ShouldNotRetainReplayLock()
    {
        const int processId = 43115;
        using var sessionManager = new SessionManager();
        DisableSessionManagerCleanupTimer(sessionManager);
        sessionManager.AddSession(processId);

        using (await sessionManager.AcquirePendingEventReplayLockAsync(processId, CancellationToken.None))
        {
        }

        sessionManager.RemoveSession(processId);
        GetPendingEventReplayLocks(sessionManager).Should().NotContainKey(processId);

        using (await sessionManager.AcquirePendingEventReplayLockAsync(processId, CancellationToken.None))
        {
            GetPendingEventReplayLocks(sessionManager).Should().NotContainKey(processId,
                "acquiring a replay lock for a removed session must not recreate retained semaphore state");
        }
    }

    [Fact]
    public void TryPeekPendingEventReplayMetadata_WithoutTimestampUtc_ShouldReturnInjectedSavedAtUtc()
    {
        const int processId = 43114;
        var currentTime = new DateTimeOffset(2026, 4, 24, 10, 15, 30, TimeSpan.Zero);
        using var sessionManager = new SessionManager(
            McpServerConfiguration.RateLimitRequestsPerMinute,
            authManager: null,
            certManager: null,
            utcNowProvider: () => currentTime);
        DisableSessionManagerCleanupTimer(sessionManager);
        sessionManager.AddSession(processId);

        sessionManager.SavePendingEventReplay(
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
                        elementId = "Replay_Button_1",
                        eventName = "Click",
                        senderType = "Button",
                        senderName = "ReplayButton",
                        routingStrategy = "Bubble",
                        handled = false,
                        originalSourceType = "Button"
                    }
                }
            }));

        sessionManager.TryPeekPendingEventReplayMetadata(processId, out var replayPayload, out var savedAtUtc).Should().BeTrue();

        savedAtUtc.Should().Be(currentTime);
        replayPayload.GetProperty("pendingEvents")[0].TryGetProperty("timestampUtc", out _).Should().BeFalse();
    }

    private static IReadOnlyDictionary<int, SemaphoreSlim> GetPendingEventReplayLocks(SessionManager sessionManager)
    {
        var field = typeof(SessionManager).GetField(
            "_pendingEventReplayLocks",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (IReadOnlyDictionary<int, SemaphoreSlim>)field.GetValue(sessionManager)!;
    }
}
