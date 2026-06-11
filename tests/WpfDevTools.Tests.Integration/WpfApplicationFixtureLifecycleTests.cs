using System.Diagnostics;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Integration;

public sealed class WpfApplicationFixtureLifecycleTests
{
    private static readonly TimeSpan DeferredSignalDisposalTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public void EnsureStartupCompleted_WhenStartupFailureExists_ShouldThrowWithInnerExceptionEvenAfterSignal()
    {
        var startupFailure = new InvalidOperationException("UI startup failed");

        var act = () => WpfApplicationFixture.EnsureStartupCompleted(
            startupCompleted: true,
            startupFailure: startupFailure,
            application: null,
            dispatcher: null,
            rootWindow: null,
            startupTimeout: TimeSpan.FromSeconds(10));

        var exception = act.Should().Throw<InvalidOperationException>().Which;
        exception.Message.Should().Be("Failed to initialize WPF Application");
        exception.InnerException.Should().BeSameAs(startupFailure);
    }

    [Fact]
    public void EnsureStartupCompleted_WhenStartupTimesOutWithoutFailure_ShouldThrowTimeoutException()
    {
        var act = () => WpfApplicationFixture.EnsureStartupCompleted(
            startupCompleted: false,
            startupFailure: null,
            application: null,
            dispatcher: null,
            rootWindow: null,
            startupTimeout: TimeSpan.FromSeconds(10));

        act.Should().Throw<TimeoutException>()
            .WithMessage("*Timed out waiting*WPF Application startup*");
    }

    [Fact]
    public void ReleaseStartupSignal_WhenShutdownIsIncomplete_ShouldDeferDisposalUntilStopSignal()
    {
        var appStarted = new ManualResetEventSlim(false);
        var appStopped = new ManualResetEventSlim(false);

        try
        {
            WpfApplicationFixture.ReleaseStartupSignal(
                shutdownCompleted: false,
                appStarted,
                appStopped);

            appStarted.Invoking(signal => signal.Set()).Should().NotThrow(
                "late startup signaling should stay safe until the UI thread has actually exited");

            appStopped.Set();

            SpinWait.SpinUntil(() => IsDisposed(appStarted), DeferredSignalDisposalTimeout).Should().BeTrue(
                "the deferred cleanup should eventually release the startup signal after the UI thread signals shutdown");
        }
        finally
        {
            if (!IsDisposed(appStarted))
            {
                appStarted.Dispose();
            }

            if (!IsDisposed(appStopped))
            {
                appStopped.Dispose();
            }
        }
    }

    [Fact]
    public void TryStartDispatcherLoop_WhenStartupWasAlreadyAborted_ShouldNotSignalOrRunDispatcher()
    {
        var signaledStartup = false;
        var ranDispatcher = false;

        var startedDispatcher = WpfApplicationFixture.TryStartDispatcherLoop(
            isStartupAborted: () => true,
            signalStarted: () => signaledStartup = true,
            runDispatcher: () => ranDispatcher = true);

        startedDispatcher.Should().BeFalse(
            "a constructor timeout should prevent a late startup thread from advertising readiness or entering a live dispatcher loop");
        signaledStartup.Should().BeFalse();
        ranDispatcher.Should().BeFalse();
    }

    [Fact]
    public void TryStartDispatcherLoop_WhenStartupAbortsAfterSignal_ShouldNotRunDispatcher()
    {
        var startupAborted = false;
        var signaledStartup = false;
        var ranDispatcher = false;

        var startedDispatcher = WpfApplicationFixture.TryStartDispatcherLoop(
            isStartupAborted: () => startupAborted,
            signalStarted: () =>
            {
                signaledStartup = true;
                startupAborted = true;
            },
            runDispatcher: () => ranDispatcher = true);

        startedDispatcher.Should().BeFalse(
            "a late abort that arrives after the waiter is released should still stop the thread before Dispatcher.Run begins");
        signaledStartup.Should().BeTrue();
        ranDispatcher.Should().BeFalse();
    }

    [Fact]
    public void CompleteShutdown_WhenThreadExitsAfterTimeout_ShouldNotDisposeSignalEarly()
    {
        using var blockingStarted = new ManualResetEventSlim(false);
        using var releaseBlock = new ManualResetEventSlim(false);
        var appStopped = new ManualResetEventSlim(false);
        Exception? backgroundFailure = null;

        var uiThread = new Thread(() =>
        {
            try
            {
                blockingStarted.Set();
                releaseBlock.Wait();
            }
            catch (Exception ex)
            {
                backgroundFailure = ex;
                throw;
            }
            finally
            {
                try
                {
                    appStopped.Set();
                }
                catch (Exception ex)
                {
                    backgroundFailure = ex;
                }
            }
        })
        {
            IsBackground = true
        };

        try
        {
            uiThread.Start();
            blockingStarted.Wait(TimeSpan.FromSeconds(1)).Should().BeTrue();

            var shutdownCompleted = false;
            Action completeShutdown = () => shutdownCompleted = WpfApplicationFixture.CompleteShutdown(
                uiThread,
                appStopped,
                TimeSpan.FromMilliseconds(50));
            completeShutdown.Should().NotThrow();
            WpfApplicationFixture.ReleaseStoppedSignal(shutdownCompleted, appStopped);

            shutdownCompleted.Should().BeFalse(
                "the helper should report that the UI thread is still alive when the shared shutdown budget expires");
            uiThread.IsAlive.Should().BeTrue(
                "the helper should return without disposing the shutdown signal when the UI thread has not exited yet");

            releaseBlock.Set();

            uiThread.Join(TimeSpan.FromSeconds(1)).Should().BeTrue(
                "the background thread should still be able to signal shutdown completion cleanly after the timeout path returns");
            backgroundFailure.Should().BeNull(
                "the delayed thread exit should not encounter an ObjectDisposedException when it signals shutdown completion");
            SpinWait.SpinUntil(() => IsDisposed(appStopped), DeferredSignalDisposalTimeout).Should().BeTrue(
                "the release step should eventually dispose the shutdown signal after the delayed thread exit completes");
        }
        finally
        {
            releaseBlock.Set();
            if (uiThread.IsAlive)
            {
                uiThread.Join(TimeSpan.FromSeconds(1));
            }

            if (!IsDisposed(appStopped))
            {
                appStopped.Dispose();
            }
        }
    }

    [Fact]
    public void CompleteShutdown_ShouldUseSingleTimeoutBudgetAcrossSignalWaitAndThreadJoin()
    {
        using var allowThreadExit = new ManualResetEventSlim(false);
        var appStopped = new ManualResetEventSlim(false);

        var uiThread = new Thread(() =>
        {
            Thread.Sleep(TimeSpan.FromMilliseconds(100));
            appStopped.Set();
            allowThreadExit.Wait();
        })
        {
            IsBackground = true
        };

        uiThread.Start();

        try
        {
            var elapsed = Stopwatch.StartNew();

            var shutdownCompleted = WpfApplicationFixture.CompleteShutdown(
                uiThread,
                appStopped,
                TimeSpan.FromMilliseconds(150));
            WpfApplicationFixture.ReleaseStoppedSignal(shutdownCompleted, appStopped);

            elapsed.Stop();

            shutdownCompleted.Should().BeFalse(
                "the background thread intentionally remains alive after the signal is raised so the helper should exhaust the shared timeout budget and return early");
            uiThread.IsAlive.Should().BeTrue(
                "the helper should return once the shared shutdown budget is exhausted even if the background thread still has not exited");
            elapsed.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(230),
                "waiting for the shutdown signal and joining the UI thread should consume one shared timeout budget instead of two full waits");
        }
        finally
        {
            allowThreadExit.Set();
            uiThread.Join(TimeSpan.FromSeconds(1)).Should().BeTrue();

            if (!IsDisposed(appStopped))
            {
                appStopped.Dispose();
            }
        }
    }

    private static bool IsDisposed(ManualResetEventSlim signal)
    {
        try
        {
            _ = signal.WaitHandle;
            return false;
        }
        catch (ObjectDisposedException)
        {
            return true;
        }
    }
}
