using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.State;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class SessionManagerStateSnapshotRetentionTests
{
    private const int ExpectedRetainedSnapshotsPerProcess = 20;

    [Fact]
    public void SaveStateSnapshot_ShouldEvictOldestSnapshotsPerProcessAndClearStaleActiveSnapshot()
    {
        const int processId = 51060;
        using var sessionManager = new SessionManager();
        DisableSessionManagerCleanupTimer(sessionManager);
        sessionManager.AddSession(processId);

        var firstSnapshot = CreateSnapshot("snapshot_00", DateTimeOffset.UtcNow.AddMinutes(-10));
        sessionManager.SaveStateSnapshot(processId, firstSnapshot);
        sessionManager.SetActiveSnapshotId(processId, firstSnapshot.SnapshotId);

        for (var index = 1; index <= ExpectedRetainedSnapshotsPerProcess; index++)
        {
            sessionManager.SaveStateSnapshot(
                processId,
                CreateSnapshot($"snapshot_{index:00}", DateTimeOffset.UtcNow.AddMinutes(index)));
        }

        sessionManager.TryGetStateSnapshot(processId, firstSnapshot.SnapshotId, out _).Should().BeFalse();
        sessionManager.TryGetActiveSnapshotId(processId, out _).Should().BeFalse();
        sessionManager.TryGetStateSnapshot(processId, "snapshot_20", out _).Should().BeTrue();
    }

    [Fact]
    public void SaveStateSnapshot_WhenTimestampsTie_ShouldRetainNewlySavedSnapshot()
    {
        const int processId = 51066;
        using var sessionManager = new SessionManager();
        DisableSessionManagerCleanupTimer(sessionManager);
        sessionManager.AddSession(processId);

        var capturedAtUtc = new DateTimeOffset(2026, 5, 18, 12, 0, 0, TimeSpan.Zero);
        for (var index = 0; index < ExpectedRetainedSnapshotsPerProcess; index++)
        {
            sessionManager.SaveStateSnapshot(
                processId,
                CreateSnapshot($"snapshot_existing_{index:00}", capturedAtUtc));
        }

        var newSnapshot = CreateSnapshot("snapshot_000_new", capturedAtUtc);
        sessionManager.SaveStateSnapshot(processId, newSnapshot);
        sessionManager.SetActiveSnapshotId(processId, newSnapshot.SnapshotId);

        sessionManager.TryGetStateSnapshot(processId, newSnapshot.SnapshotId, out _).Should().BeTrue();
        sessionManager.TryGetActiveSnapshotId(processId, out var activeSnapshotId).Should().BeTrue();
        activeSnapshotId.Should().Be(newSnapshot.SnapshotId);
    }

    [Fact]
    public void SaveStateSnapshot_ShouldEvictExpiredSnapshotsAndClearStaleActiveSnapshot()
    {
        const int processId = 51061;
        var currentTime = new DateTimeOffset(2026, 5, 18, 9, 0, 0, TimeSpan.Zero);
        using var sessionManager = new SessionManager(
            McpServerConfiguration.RateLimitRequestsPerMinute,
            authManager: null,
            certManager: null,
            utcNowProvider: () => currentTime);
        DisableSessionManagerCleanupTimer(sessionManager);
        sessionManager.AddSession(processId);

        var expiredSnapshot = CreateSnapshot("snapshot_expired", currentTime);
        sessionManager.SaveStateSnapshot(processId, expiredSnapshot);
        sessionManager.SetActiveSnapshotId(processId, expiredSnapshot.SnapshotId);
        currentTime = currentTime.AddMinutes(31);

        sessionManager.SaveStateSnapshot(
            processId,
            CreateSnapshot("snapshot_fresh", currentTime));

        sessionManager.TryGetStateSnapshot(processId, expiredSnapshot.SnapshotId, out _).Should().BeFalse();
        sessionManager.TryGetActiveSnapshotId(processId, out _).Should().BeFalse();
        sessionManager.TryGetStateSnapshot(processId, "snapshot_fresh", out _).Should().BeTrue();
    }

    [Fact]
    public void TryGetActiveSnapshotId_ShouldClearExpiredActiveSnapshot()
    {
        const int processId = 51062;
        var currentTime = new DateTimeOffset(2026, 5, 18, 10, 0, 0, TimeSpan.Zero);
        using var sessionManager = new SessionManager(
            McpServerConfiguration.RateLimitRequestsPerMinute,
            authManager: null,
            certManager: null,
            utcNowProvider: () => currentTime);
        DisableSessionManagerCleanupTimer(sessionManager);
        sessionManager.AddSession(processId);

        var snapshot = CreateSnapshot("snapshot_active", currentTime);
        sessionManager.SaveStateSnapshot(processId, snapshot);
        sessionManager.SetActiveSnapshotId(processId, snapshot.SnapshotId);
        currentTime = currentTime.AddMinutes(31);

        sessionManager.TryGetActiveSnapshotId(processId, out _).Should().BeFalse();
    }

    [Fact]
    public void TryGetNavigationState_ShouldClearExpiredActiveSnapshot()
    {
        const int processId = 51064;
        var currentTime = new DateTimeOffset(2026, 5, 18, 11, 0, 0, TimeSpan.Zero);
        using var sessionManager = new SessionManager(
            McpServerConfiguration.RateLimitRequestsPerMinute,
            authManager: null,
            certManager: null,
            utcNowProvider: () => currentTime);
        DisableSessionManagerCleanupTimer(sessionManager);
        sessionManager.AddSession(processId);

        var snapshot = CreateSnapshot("snapshot_navigation", currentTime);
        sessionManager.SaveStateSnapshot(processId, snapshot);
        sessionManager.SetActiveSnapshotId(processId, snapshot.SnapshotId);
        currentTime = currentTime.AddMinutes(31);

        sessionManager.TryGetNavigationState(processId, out var state).Should().BeTrue();
        state!.ActiveSnapshotId.Should().BeNull();
    }

    [Fact]
    public void TryGetActiveSnapshotId_ShouldClearMissingActiveSnapshot()
    {
        const int processId = 51063;
        using var sessionManager = new SessionManager();
        DisableSessionManagerCleanupTimer(sessionManager);
        sessionManager.AddSession(processId);
        sessionManager.SetActiveSnapshotId(processId, "snapshot_missing");

        sessionManager.TryGetActiveSnapshotId(processId, out _).Should().BeFalse();
    }

    [Fact]
    public void TryGetNavigationState_ShouldClearMissingActiveSnapshot()
    {
        const int processId = 51065;
        using var sessionManager = new SessionManager();
        DisableSessionManagerCleanupTimer(sessionManager);
        sessionManager.AddSession(processId);
        sessionManager.SetActiveSnapshotId(processId, "snapshot_missing");

        sessionManager.TryGetNavigationState(processId, out var state).Should().BeTrue();
        state!.ActiveSnapshotId.Should().BeNull();
    }

    private static StoredStateSnapshot CreateSnapshot(string snapshotId, DateTimeOffset capturedAtUtc) =>
        new(
            snapshotId,
            SnapshotName: null,
            ElementId: null,
            DependencyProperties: Array.Empty<StoredDependencyPropertySnapshot>(),
            ViewModelProperties: Array.Empty<StoredViewModelPropertySnapshot>(),
            Focus: null,
            BindingErrors: Array.Empty<StoredBindingErrorSnapshot>(),
            HasBindingErrorBaseline: true,
            ValidationErrors: Array.Empty<StoredValidationErrorSnapshot>(),
            HasValidationBaseline: true,
            capturedAtUtc);
}
