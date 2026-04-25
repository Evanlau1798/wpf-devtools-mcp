using System.Diagnostics;
using FluentAssertions;
using WpfDevTools.Tests.Integration.TestSupport;

namespace WpfDevTools.Tests.Integration;

public sealed class TestAppProcessLauncherTests
{
    [Fact]
    public void WaitForMainWindowCore_ShouldUseSingleTimeoutBudgetAcrossInputIdleAndPolling()
    {
        var elapsed = Stopwatch.StartNew();

        var result = TestAppProcessLauncher.WaitForMainWindowCore(
            TimeSpan.FromMilliseconds(200),
            remainingTimeout =>
            {
                Thread.Sleep(remainingTimeout + TimeSpan.FromMilliseconds(50));
                return false;
            },
            () => new TestAppProcessLauncher.ProcessWindowState(HasExited: false, HasMainWindow: false),
            static _ => throw new InvalidOperationException("Polling should not start after the shared timeout budget is exhausted."));

        elapsed.Stop();

        result.Should().BeFalse();
        elapsed.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(350),
            "input-idle waiting and fallback polling should share one timeout budget instead of consuming separate full timeouts");
    }

    [Fact]
    public void WaitForMainWindowCore_WhenMainWindowAppearsBeforeInputIdleSettles_ShouldDetectIt()
    {
        var elapsed = Stopwatch.StartNew();

        var result = TestAppProcessLauncher.WaitForMainWindowCore(
            TimeSpan.FromMilliseconds(400),
            remainingTimeout =>
            {
                Thread.Sleep(remainingTimeout);
                return false;
            },
            () => new TestAppProcessLauncher.ProcessWindowState(
                HasExited: false,
                HasMainWindow: elapsed.Elapsed >= TimeSpan.FromMilliseconds(150)),
            static _ => throw new InvalidOperationException("Polling should not be required once the window is visible after the bounded input-idle grace period."));

        elapsed.Stop();

        result.Should().BeTrue(
            "a visible main window should be accepted even when the process has not yet become fully input-idle");
        elapsed.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(350),
            "the launcher should re-check window state after a capped input-idle grace period instead of consuming the full startup budget first");
    }
}