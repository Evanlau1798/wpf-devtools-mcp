using System.Text.Json;
using FluentAssertions;
using Xunit.Sdk;

namespace WpfDevTools.Tests.Integration.E2E;

public sealed class E2eTestHelpersLifecycleTests
{
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
    public void AssertFixtureReady_WhenFixtureIsQuarantined_ShouldSurfaceQuarantineReason()
    {
        var fixture = new McpE2eFixture();
        fixture.Quarantine("Shared E2E reset failed during test cleanup: drain_events cleanup failed");

        Action act = () => E2eTestHelpers.AssertFixtureReady(fixture);

        act.Should().Throw<XunitException>()
            .WithMessage("*quarantined*Shared E2E reset failed during test cleanup*");
    }
}