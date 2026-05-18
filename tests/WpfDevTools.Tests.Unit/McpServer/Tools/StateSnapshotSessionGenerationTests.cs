using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.State;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.ErrorHandling;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public sealed class StateSnapshotSessionGenerationTests
{
    [Fact]
    public void SaveStateSnapshot_ShouldPersistCurrentSessionGenerationOnStoredSnapshot()
    {
        const int processId = 51141;
        using var sessionManager = new SessionManager();
        DisableSessionManagerCleanupTimer(sessionManager);
        sessionManager.AddSession(processId);
        sessionManager.TryGetSessionGeneration(processId, out var sessionGeneration).Should().BeTrue();

        sessionManager.SaveStateSnapshot(
            processId,
            CreateStoredStateSnapshot("snapshot_generation", DateTimeOffset.UtcNow));

        sessionManager.TryGetStateSnapshot(processId, "snapshot_generation", out var snapshot).Should().BeTrue();
        var generationProperty = snapshot!.GetType().GetProperty("SessionGeneration");
        generationProperty.Should().NotBeNull("stored snapshots must be bound to the session generation that created them");
        generationProperty!.GetValue(snapshot).Should().Be(sessionGeneration);
    }

    [Fact]
    public void SaveStateSnapshot_WhenExpectedSessionGenerationIsStale_ShouldRejectSnapshot()
    {
        const int processId = 51140;
        using var sessionManager = new SessionManager();
        DisableSessionManagerCleanupTimer(sessionManager);
        sessionManager.AddSession(processId);
        sessionManager.TryGetSessionGeneration(processId, out var staleGeneration).Should().BeTrue();
        sessionManager.RemoveSession(processId);
        sessionManager.AddSession(processId);

        var saved = sessionManager.SaveStateSnapshot(
            processId,
            CreateStoredStateSnapshot("snapshot_stale_save", DateTimeOffset.UtcNow),
            staleGeneration);

        saved.Should().BeFalse();
        sessionManager.TryGetStateSnapshot(processId, "snapshot_stale_save", out _).Should().BeFalse();
    }

    [Fact]
    public async Task GetStateDiff_WhenSnapshotGenerationIsStale_ShouldRejectAndClearSnapshotReference()
    {
        const int processId = 51142;
        const string snapshotId = "snapshot_stale_diff";
        using var sessionManager = new SessionManager();
        DisableSessionManagerCleanupTimer(sessionManager);
        sessionManager.AddSession(processId);

        InsertStoredSnapshot(
            sessionManager,
            processId,
            CreateStoredStateSnapshot(snapshotId, DateTimeOffset.UtcNow) with { SessionGeneration = 0 });
        sessionManager.SetActiveSnapshotId(processId, snapshotId);

        var result = JsonSerializer.SerializeToElement(await new GetStateDiffTool(sessionManager).ExecuteAsync(ToJsonElement(new
        {
            processId,
            snapshotId
        }), CancellationToken.None));

        result.GetProperty("errorCode").GetString().Should().Be(ToolErrorCode.InvalidArgument.ToString());
        sessionManager.TryGetStateSnapshot(processId, snapshotId, out _).Should().BeFalse();
        sessionManager.TryGetActiveSnapshotId(processId, out _).Should().BeFalse();
    }

    private static void InsertStoredSnapshot(
        SessionManager sessionManager,
        int processId,
        StoredStateSnapshot snapshot)
    {
        var field = typeof(SessionManager).GetField(
            "_stateSnapshots",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var snapshotsByProcess = (Dictionary<int, Dictionary<string, StoredStateSnapshot>>)field.GetValue(sessionManager)!;
        snapshotsByProcess[processId][snapshot.SnapshotId] = snapshot;
    }
}
