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
    private readonly IDisposable _plaintextPolicy;

    public InspectorHostLifecycleReviewTests()
    {
        _plaintextPolicy = UnsafePlaintextInspectorHostTestEnvironment.BeginScope();
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
        _plaintextPolicy.Dispose();
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

    [Fact]
    public void CompleteStartupFailure_ShouldSignalSharedStartupTaskOnce()
    {
        var content = ReadInspectorHostSource();
        var method = GetMethodBody(content, "private void CompleteStartupFailure", "private void WaitForStopCompletion");

        CountOccurrences(method, "TrySetException(startupError)").Should().Be(1,
            "startup failure should have a single authoritative task signal before rethrowing to the owning caller");
    }

    [Fact]
    public void CompleteStopAsync_ShouldAwaitServerTaskShutdownWithoutBlockingThreadPoolThread()
    {
        var content = ReadInspectorHostSource();
        var method = GetMethodBody(content, "private async Task CompleteStopAsync", "private void CompleteStopFinalization");

        method.Should().Contain("WaitForServerTaskShutdownAsync",
            "stop cleanup should wait asynchronously instead of blocking a thread-pool thread");
        method.Should().NotContain(".Wait(",
            "stop cleanup runs on the thread pool and should not block that worker while waiting for server shutdown");
    }

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);

    private static string ReadInspectorHostSource()
    {
        var directory = GetRepoFilePath("src/WpfDevTools.Inspector/Host");
        var files = Directory.GetFiles(directory, "InspectorHost*.cs", SearchOption.TopDirectoryOnly)
            .Order(StringComparer.Ordinal)
            .ToArray();

        return string.Join(Environment.NewLine, files.Select(File.ReadAllText));
    }

    private static string GetMethodBody(string content, string startMarker, string endMarker)
    {
        var start = content.IndexOf(startMarker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);

        var end = content.IndexOf(endMarker, start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start);

        return content[start..end];
    }

    private static int CountOccurrences(string content, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = content.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
