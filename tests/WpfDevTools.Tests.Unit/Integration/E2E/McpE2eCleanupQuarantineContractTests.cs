using FluentAssertions;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Integration.E2E;

public sealed class McpE2eCleanupQuarantineContractTests
{
    [Fact]
    public void E2eTestHelpers_ShouldPromoteCleanupIncompleteDiagnosticsDuringSharedReset()
    {
        var content = File.ReadAllText(
            TestRepositoryPaths.GetRepoFilePath("tests/WpfDevTools.Tests.Integration/E2E/E2eTestHelpers.cs"));

        content.Should().Contain("cleanupIncomplete",
            "shared-session E2E reset should not silently treat cleanup diagnostics as a successful drain_events reset");
        content.Should().Contain("cleanupFailureMessage",
            "cleanup diagnostics should preserve the actionable failure detail when reset drains report incomplete cleanup");
        content.Should().Contain("cleanupFailureType",
            "cleanup diagnostics should preserve the failure category when reset drains report incomplete cleanup");
    }

    [Fact]
    public void SharedStateMcpE2eTestBase_ShouldQuarantineFixtureAfterResetFailure()
    {
        var content = File.ReadAllText(
            TestRepositoryPaths.GetRepoFilePath("tests/WpfDevTools.Tests.Integration/E2E/SharedStateMcpE2eTestBase.cs"));

        content.Should().Contain("E2eTestHelpers.AssertFixtureReady(Fixture)",
            "shared-session tests should fail fast when the shared E2E lane has already been quarantined by a prior cleanup failure");
        content.Should().Contain("Fixture.Quarantine(",
            "reset failures should quarantine the shared E2E fixture so later tests do not continue against contaminated state");
        content.Should().Contain("during test initialization",
            "the quarantine diagnostic should identify initialization resets separately from cleanup resets");
        content.Should().Contain("during test cleanup",
            "the quarantine diagnostic should identify post-test cleanup resets separately from initialization resets");
        content.Should().Contain("ResetSharedSessionStateAsync(Fixture)",
            "shared-session reset must rebuild the MCP session so snapshot and trace navigation state do not leak across tests");
    }

    [Fact]
    public void E2eTestHelpers_ShouldReconnectTheSharedMcpSessionBeforeResettingAppState()
    {
        var content = File.ReadAllText(
            TestRepositoryPaths.GetRepoFilePath("tests/WpfDevTools.Tests.Integration/E2E/E2eTestHelpers.cs"));

        content.Should().Contain("ReconnectClientAsync()",
            "shared-session E2E reset should recreate the MCP session so session-scoped navigation state is cleared between tests");
        content.Should().Contain("ResetTestAppStateAsync(fixture.Client, fixture.TestAppProcessId)",
            "shared-session reset should still restore the TestApp baseline after rebuilding the MCP session");
    }
}