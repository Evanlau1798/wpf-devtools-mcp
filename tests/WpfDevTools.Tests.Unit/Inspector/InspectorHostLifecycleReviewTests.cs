using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Host;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector;

[Collection("InspectorHostLifecycle")]
public sealed class InspectorHostLifecycleReviewTests : IDisposable
{
    private readonly Action _originalResetMonitoringAction;
    private readonly Action _originalStopAllWatchersAction;
    private readonly Action _originalUninstallBindingTraceListenerAction;

    public InspectorHostLifecycleReviewTests()
    {
        _originalResetMonitoringAction = InspectorHost.ResetMonitoringAction;
        _originalStopAllWatchersAction = InspectorHost.StopAllWatchersAction;
        _originalUninstallBindingTraceListenerAction = InspectorHost.UninstallBindingTraceListenerAction;
        InspectorHost.ResetMonitoringAction = static () => PerformanceAnalyzer.ResetMonitoring();
        InspectorHost.StopAllWatchersAction = static () => DependencyPropertyAnalyzer.StopAllWatchers();
        InspectorHost.UninstallBindingTraceListenerAction = static () => BindingErrorTraceListener.Uninstall();
    }

    public void Dispose()
    {
        InspectorHost.ResetMonitoringAction = _originalResetMonitoringAction;
        InspectorHost.StopAllWatchersAction = _originalStopAllWatchersAction;
        InspectorHost.UninstallBindingTraceListenerAction = _originalUninstallBindingTraceListenerAction;
    }

    [Fact]
    public async Task Start_WhenPreviousStopCleanupIsStillRunning_ShouldWaitForStopFinalization()
    {
        var pid = global::WpfDevTools.Tests.Unit.TestHelpers.NextSyntheticProcessId();
        using var cleanupStarted = new ManualResetEventSlim(initialState: false);
        using var releaseCleanup = new ManualResetEventSlim(initialState: false);
        using var host = new InspectorHost(pid);
        host.Start();

        InspectorHost.ResetMonitoringAction = () =>
        {
            cleanupStarted.Set();
            releaseCleanup.Wait(TimeSpan.FromSeconds(5));
        };

        await Task.Run(() => host.Stop());
        cleanupStarted.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue();

        var restartTask = Task.Run(() => host.Start());
        await Task.Delay(100);

        restartTask.IsCompleted.Should().BeFalse("restart should wait for the previous stop finalization before starting a new server loop");

        releaseCleanup.Set();
        await restartTask;

        host.IsRunning.Should().BeTrue();
    }

    [Fact]
    public async Task Start_WhenDisposeIsInProgress_ShouldThrowObjectDisposedException()
    {
        var pid = global::WpfDevTools.Tests.Unit.TestHelpers.NextSyntheticProcessId();
        using var cleanupStarted = new ManualResetEventSlim(initialState: false);
        using var releaseCleanup = new ManualResetEventSlim(initialState: false);
        using var host = new InspectorHost(pid);
        host.Start();

        InspectorHost.ResetMonitoringAction = () =>
        {
            cleanupStarted.Set();
            releaseCleanup.Wait(TimeSpan.FromSeconds(5));
        };

        var disposeTask = Task.Run(() => host.Dispose());
        cleanupStarted.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue();

        var startTask = Task.Run(() => host.Start());

        var exception = await Assert.ThrowsAsync<ObjectDisposedException>(() => startTask);
        exception.ObjectName.Should().Be(nameof(InspectorHost));

        releaseCleanup.Set();
        await disposeTask;
    }
}