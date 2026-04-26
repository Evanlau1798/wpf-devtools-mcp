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

    [Fact]
    public async Task ResetTestAppStateAsync_WhenDrainEventsReportsCleanupIncomplete_ShouldFailFastWithCleanupDetails()
    {
        var drainEventsCalls = 0;

        Task<JsonElement> CallToolAsync(string toolName, object? arguments)
        {
            if (toolName == "drain_events")
            {
                drainEventsCalls++;
                return Task.FromResult(JsonSerializer.SerializeToElement(new
                {
                    success = true,
                    cleanupIncomplete = true,
                    cleanupFailureMessage = "cleanup failed",
                    cleanupFailureType = "InvalidOperationException"
                }));
            }

            return Task.FromResult(toolName switch
            {
                "get_namescope" => NamescopeResult(),
                "click_element" or "execute_command" => SuccessResult(),
                _ => throw new InvalidOperationException($"Unexpected tool call: {toolName}")
            });
        }

        Func<Task> act = () => E2eTestHelpers.ResetTestAppStateAsync(
            CallToolAsync,
            processId: 123);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*drain_events*cleanupIncomplete*InvalidOperationException*cleanup failed*");
        drainEventsCalls.Should().Be(1, "cleanupIncomplete should fail the reset without retrying contaminated cleanup");
    }

    [Fact]
    public async Task ResetSharedSessionStateAsync_ShouldReconnectBeforeResettingWithCurrentSession()
    {
        var session = "old";
        var calls = new List<string>();
        var fixture = new McpE2eFixture(
            testAppProcessId: 456,
            reconnectClientAsync: () =>
            {
                calls.Add("reconnect");
                session = "new";
                return Task.CompletedTask;
            },
            callToolAsync: (toolName, _) =>
            {
                calls.Add($"{session}:{toolName}");
                session.Should().Be("new", "shared reset must use the rebuilt MCP session so session-scoped state is cleared");

                return Task.FromResult(toolName switch
                {
                    "get_namescope" => NamescopeResult(),
                    "click_element" or "execute_command" or "drain_events" => SuccessResult(),
                    _ => throw new InvalidOperationException($"Unexpected tool call: {toolName}")
                });
            });

        await E2eTestHelpers.ResetSharedSessionStateAsync(fixture);

        calls.Should().Equal(
            "reconnect",
            "new:get_namescope",
            "new:get_namescope",
            "new:click_element",
            "new:execute_command",
            "new:drain_events");
    }

    [Fact]
    public async Task SharedStateBaseInitializeAsync_WhenResetFails_ShouldQuarantineFixtureWithVisibleReason()
    {
        var fixture = new McpE2eFixture();
        var test = new TestableSharedStateMcpE2eTest(
            fixture,
            _ => throw new InvalidOperationException("drain_events cleanup failed"));

        Func<Task> act = () => test.InitializeAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("drain_events cleanup failed");
        fixture.QuarantineReason.Should().Be(
            "Shared E2E reset failed during test initialization: drain_events cleanup failed");

        Action assertReady = () => E2eTestHelpers.AssertFixtureReady(fixture);
        assertReady.Should().Throw<XunitException>()
            .WithMessage("*Shared E2E reset failed during test initialization*");
    }

    [Fact]
    public async Task SharedStateBaseDisposeAsync_WhenResetFails_ShouldQuarantineFixtureWithVisibleReason()
    {
        var fixture = new McpE2eFixture();
        var test = new TestableSharedStateMcpE2eTest(
            fixture,
            _ => throw new InvalidOperationException("drain_events cleanup failed"));

        Func<Task> act = () => test.DisposeAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("drain_events cleanup failed");
        fixture.QuarantineReason.Should().Be(
            "Shared E2E reset failed during test cleanup: drain_events cleanup failed");

        Action assertReady = () => E2eTestHelpers.AssertFixtureReady(fixture);
        assertReady.Should().Throw<XunitException>()
            .WithMessage("*Shared E2E reset failed during test cleanup*");
    }

    [Fact]
    public async Task SharedStateBaseInitializeAsync_WhenFixtureAlreadyQuarantined_ShouldFailFastWithoutReset()
    {
        var resetCalled = false;
        var fixture = new McpE2eFixture();
        fixture.Quarantine("Shared E2E reset failed during test cleanup: drain_events cleanup failed");
        var test = new TestableSharedStateMcpE2eTest(
            fixture,
            _ =>
            {
                resetCalled = true;
                return Task.CompletedTask;
            });

        Func<Task> act = () => test.InitializeAsync();

        await act.Should().ThrowAsync<XunitException>()
            .WithMessage("*Shared E2E reset failed during test cleanup*");
        resetCalled.Should().BeFalse("quarantined shared fixtures should stop before attempting another reset");
    }

    [Fact]
    public async Task SharedStateBaseDisposeAsync_WhenFixtureAlreadyQuarantined_ShouldNotResetAgain()
    {
        var resetCalled = false;
        var fixture = new McpE2eFixture();
        fixture.Quarantine("Shared E2E reset failed during test initialization: drain_events cleanup failed");
        var test = new TestableSharedStateMcpE2eTest(
            fixture,
            _ =>
            {
                resetCalled = true;
                return Task.CompletedTask;
            });

        await test.DisposeAsync();

        resetCalled.Should().BeFalse("cleanup after a quarantined initialization failure should be deterministic");
    }

    private static JsonElement SuccessResult()
        => JsonSerializer.SerializeToElement(new
        {
            success = true,
            pendingEventCount = 0
        });

    private static JsonElement NamescopeResult()
        => JsonSerializer.SerializeToElement(new
        {
            success = true,
            namedElements = new[]
            {
                new { name = "BasicControlsTab", elementId = "tab-id" },
                new { name = "NameTextBox", elementId = "name-id" }
            }
        });

    private sealed class TestableSharedStateMcpE2eTest : SharedStateMcpE2eTestBase
    {
        public TestableSharedStateMcpE2eTest(
            McpE2eFixture fixture,
            Func<McpE2eFixture, Task> resetSharedSessionStateAsync)
            : base(fixture, resetSharedSessionStateAsync)
        {
        }
    }
}
