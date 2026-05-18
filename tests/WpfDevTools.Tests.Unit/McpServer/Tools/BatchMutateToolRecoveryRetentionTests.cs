using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public sealed class BatchMutateToolRecoveryRetentionTests
{
    [Fact]
    public async Task ExecuteAsync_WhenCapturedSnapshotExpiresBeforeFailure_ShouldReturnManualRecovery()
    {
        const int processId = 12345;
        var currentTime = new DateTimeOffset(2026, 5, 18, 9, 0, 0, TimeSpan.Zero);
        using var sessionManager = CreateTimedSessionManager(() => currentTime);
        sessionManager.AddSession(processId);

        var tool = new BatchMutateTool(
            sessionManager,
            (_, _, _) =>
            {
                currentTime = currentTime.AddMinutes(31);
                return Task.FromResult<object>(new
                {
                    success = false,
                    error = "Setter failed.",
                    errorCode = "OperationFailed"
                });
            },
            (args, _) => Task.FromResult(SaveStoredSnapshotResult(
                sessionManager,
                args,
                "snapshot_batch_expired",
                currentTime)),
            null);

        var result = JsonSerializer.SerializeToElement(await ExecuteFailingBatch(tool, processId));

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        AssertManualRecovery(result, "no longer retained");
    }

    [Fact]
    public async Task ExecuteAsync_WhenCapturedSnapshotIsEvictedBeforeFailure_ShouldReturnManualRecovery()
    {
        const int processId = 12346;
        var currentTime = new DateTimeOffset(2026, 5, 18, 10, 0, 0, TimeSpan.Zero);
        using var sessionManager = CreateTimedSessionManager(() => currentTime);
        sessionManager.AddSession(processId);

        var tool = new BatchMutateTool(
            sessionManager,
            (_, _, _) =>
            {
                for (var index = 0; index < SessionManager.MaxRetainedStateSnapshotsPerProcess; index++)
                {
                    currentTime = currentTime.AddSeconds(1);
                    sessionManager.SaveStateSnapshot(
                        processId,
                        CreateStoredStateSnapshot($"snapshot_eviction_{index:00}", currentTime));
                }

                return Task.FromResult<object>(new
                {
                    success = false,
                    error = "Setter failed.",
                    errorCode = "OperationFailed"
                });
            },
            (args, _) => Task.FromResult(SaveStoredSnapshotResult(
                sessionManager,
                args,
                "snapshot_batch_evicted",
                currentTime)),
            null);

        var result = JsonSerializer.SerializeToElement(await ExecuteFailingBatch(tool, processId));

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        AssertManualRecovery(result, "no longer retained");
    }

    private static SessionManager CreateTimedSessionManager(Func<DateTimeOffset> utcNowProvider)
    {
        var sessionManager = new SessionManager(
            McpServerConfiguration.RateLimitRequestsPerMinute,
            authManager: null,
            certManager: null,
            utcNowProvider);
        DisableSessionManagerCleanupTimer(sessionManager);
        return sessionManager;
    }

    private static Task<object> ExecuteFailingBatch(BatchMutateTool tool, int processId) =>
        tool.ExecuteAsync(
            ToJsonElement(new
            {
                processId,
                captureSnapshot = new
                {
                    viewModelPropertyNames = new[] { "Name" }
                },
                mutations = new object[]
                {
                    new { tool = "modify_viewmodel", args = new { propertyName = "Name", value = "Updated" } }
                }
            }),
            CancellationToken.None);

    private static void AssertManualRecovery(JsonElement result, string expectedHintFragment)
    {
        var rollback = result.GetProperty("rollback");
        rollback.GetProperty("available").GetBoolean().Should().BeFalse();
        rollback.TryGetProperty("tool", out _).Should().BeFalse();

        var recovery = result.GetProperty("recovery");
        recovery.TryGetProperty("tool", out _).Should().BeFalse();
        recovery.GetProperty("suggestedAction").GetString().Should().Contain("manually reverse");
        recovery.GetProperty("hint").GetString().Should().Contain(expectedHintFragment);
    }
}
