using System.Text.Json;
using FluentAssertions;
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
}