using System.Text.Json;
using FluentAssertions;
using Xunit.Sdk;

namespace WpfDevTools.Tests.Integration.E2E;

public sealed class E2eTestHelpersLifecycleTests
{
    [Fact]
    public async Task DrainPendingEventsUntilEmptyAsync_WhenEventsRemain_ShouldContinueUntilEmpty()
    {
        var pendingCounts = new Queue<int>([2, 1, 0]);
        var callCount = 0;

        await E2eTestHelpers.DrainPendingEventsUntilEmptyAsync(() =>
        {
            callCount++;
            return Task.FromResult(JsonSerializer.SerializeToElement(new
            {
                success = true,
                pendingEventCount = pendingCounts.Dequeue()
            }));
        });

        callCount.Should().Be(3);
    }

    [Fact]
    public void EnsureToolSucceeded_WhenCleanupIncompleteIsReported_ShouldThrowActionableResetFailure()
    {
        var result = JsonSerializer.SerializeToElement(new
        {
            success = true,
            cleanupIncomplete = true,
            cleanupFailureMessage = "cleanup failed",
            cleanupFailureType = "InvalidOperationException"
        });

        Action act = () => E2eTestHelpers.EnsureToolSucceeded(result, "drain_events", "pending event queue");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*cleanupIncomplete*InvalidOperationException*cleanup failed*");
    }

    [Fact]
    public async Task RunWithRestoredSnapshotAsync_WhenBodyThrows_ShouldRestoreSnapshotBeforeRethrowing()
    {
        var calls = new List<string>();
        object? restoreArguments = null;

        Task<JsonElement> CallToolAsync(string toolName, object? arguments)
        {
            calls.Add(toolName);
            restoreArguments = arguments;

            return Task.FromResult(JsonSerializer.SerializeToElement(new { success = true }));
        }

        Func<Task> act = () => E2eTestHelpers.RunWithRestoredSnapshotAsync(
            CallToolAsync,
            processId: 456,
            snapshotId: "snapshot_abc",
            bodyAsync: () =>
            {
                calls.Add("body");
                throw new InvalidOperationException("mutation failed");
            });

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("mutation failed");

        calls.Should().Equal("body", "restore_state_snapshot");

        var restoreJson = JsonSerializer.SerializeToElement(restoreArguments);
        restoreJson.GetProperty("processId").GetInt32().Should().Be(456);
        restoreJson.GetProperty("snapshotId").GetString().Should().Be("snapshot_abc");
        restoreJson.GetProperty("removeAfterRestore").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void AssertFixtureReady_WhenFixtureIsQuarantined_ShouldSurfaceQuarantineReason()
    {
        var fixture = new McpE2eFixture();
        fixture.Quarantine("Shared E2E reset failed during test cleanup: drain_events cleanup failed");

        Action act = () => E2eTestHelpers.AssertFixtureReady(fixture);

        act.Should().Throw<XunitException>()
            .WithMessage("*quarantined*Shared E2E reset failed during test cleanup*");
    }
}
