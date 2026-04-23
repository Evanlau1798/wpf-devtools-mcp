using FluentAssertions;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Integration;

public sealed class FixtureLifecycleContractTests
{
    [Fact]
    public void TestAppProcessLauncher_ShouldAttemptInputIdleBeforeSleepPolling()
    {
        var content = File.ReadAllText(
            TestRepositoryPaths.GetRepoFilePath("tests/WpfDevTools.Tests.Integration/TestSupport/TestAppProcessLauncher.cs"));

        content.Should().Contain("WaitForMainWindowCore(",
            "the E2E fixture startup path should centralize its bounded wait budget in a dedicated lifecycle helper");
        content.Should().Contain("WaitForInputIdle(process, remainingTimeout)",
            "the process-specific startup path should wait for the WPF process to become input-idle before falling back to main-window polling");
        content.Should().Contain("GetRemainingTimeout(timeout, stopwatch)",
            "input-idle waiting and fallback polling should share one timeout budget instead of consuming separate full timeouts");
    }

    [Fact]
    public void WpfApplicationFixture_ShouldTrackUiThreadShutdownWithDedicatedSignal()
    {
        var content = File.ReadAllText(
            TestRepositoryPaths.GetRepoFilePath("tests/WpfDevTools.Tests.Integration/WpfApplicationFixture.cs"));

        content.Should().Contain("private readonly ManualResetEventSlim _appStopped",
            "the WPF fixture should expose an explicit shutdown-completion signal rather than relying only on thread joins");
        content.Should().Contain("_appStopped.Set();",
            "the UI thread should signal shutdown completion when the dispatcher exits");
        content.Should().Contain("CompleteShutdown(",
            "fixture disposal should centralize shutdown waiting in a dedicated lifecycle helper");
        content.Should().Contain("appStopped.Wait(remainingTimeout)",
            "the shutdown helper should wait on the lifecycle signal before joining the UI thread");
        content.Should().Contain("GetRemainingTimeout(shutdownTimeout, stopwatch)",
            "the lifecycle signal wait and thread join should share one timeout budget on slow teardown paths");
        content.Should().Contain("ReleaseStoppedSignal(",
            "fixture disposal should keep shutdown-signal ownership in a single follow-up release step");
        content.Should().Contain("ScheduleDeferredSignalDisposal(appStopped)",
            "slow teardown paths should still guarantee that the shutdown signal is eventually released after the UI thread exits");
    }

    [Fact]
    public void WpfApplicationFixture_ShouldFailFastWhenStartupDoesNotComplete()
    {
        var content = File.ReadAllText(
            TestRepositoryPaths.GetRepoFilePath("tests/WpfDevTools.Tests.Integration/WpfApplicationFixture.cs"));

        content.Should().Contain("var startupCompleted = _appStarted.Wait(DefaultStartupTimeout);",
            "fixture startup should check whether the startup signal actually arrived before treating the UI thread as ready");
        content.Should().Contain("EnsureStartupCompleted(",
            "constructor startup validation should be centralized so timeout and partial-startup checks stay aligned");
        content.Should().Contain("throw new TimeoutException(",
            "slow startup paths should fail fast with an explicit timeout instead of silently accepting a non-null dispatcher");
        content.Should().Contain("application == null || dispatcher == null || rootWindow == null",
            "startup readiness should require the full WPF surface, not just a dispatcher reference");
        content.Should().Contain("_startupFailure = ex;",
            "startup faults on the UI thread should be preserved so constructor failures are not misreported as timeouts");
        content.Should().Contain("_appStarted.Set();",
            "startup faults on the UI thread should also release the constructor waiter so failures do not sit until the full startup timeout expires");
        content.Should().Contain("_startupAborted = true;",
            "constructor timeouts should mark startup as aborted before cleanup so the late UI thread cannot transition into a live dispatcher after the fixture already failed");
        content.Should().Contain("TryStartDispatcherLoop(",
            "the UI-thread startup path should re-check the aborted flag before signaling readiness and before entering Dispatcher.Run");
        content.Should().Contain("ReleaseFailedStartupResources(",
            "constructor failures should still release the partial UI-thread resources instead of leaking a background startup thread");
        content.Should().Contain("ReleaseStartupSignal(",
            "failed-startup cleanup should defer startup-signal disposal until the UI thread has fully exited so late startup signaling cannot hit a disposed event");
    }
}