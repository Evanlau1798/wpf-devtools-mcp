using System.IO.Pipes;
using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Inspector.Analyzers;
using WpfDevTools.Inspector.Host;
using WpfDevTools.Shared.Enums;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Serialization;
using WpfDevTools.Shared.Utilities;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector;

/// <summary>
/// Tests for InspectorHost concurrency and shutdown issues
/// </summary>
[Collection("InspectorHostLifecycle")]
public partial class InspectorHostConcurrencyTests : IDisposable
{
    private static readonly TimeSpan SignalTimeout = TimeSpan.FromSeconds(10);

    private readonly Action _originalResetMonitoringAction;
    private readonly Action _originalStopAllWatchersAction;
    private readonly Action _originalUninstallBindingTraceListenerAction;
    private readonly IDisposable _plaintextPolicy;

    public InspectorHostConcurrencyTests()
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
    public async Task Stop_DuringActiveRequest_ShouldWaitForCompletion()
    {
        var pid = global::WpfDevTools.Tests.Unit.TestHelpers.NextSyntheticProcessId();
        using var host = new InspectorHost(pid);
        host.Start();
        using var client = await ConnectToHostAsync(pid);

        var request = new InspectorRequest
        {
            Id = "shutdown-test-1",
            Method = "ping",
            Params = null
        };

        var requestJson = JsonSerializer.Serialize(request);
        await MessageFraming.WriteMessageAsync(client, requestJson);

        var stopTask = Task.Run(() => host.Stop());

        await stopTask.WaitAsync(SignalTimeout);

        host.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void Stop_ShouldLogTimeoutIfTaskDoesNotComplete()
    {
        var pid = global::WpfDevTools.Tests.Unit.TestHelpers.NextSyntheticProcessId();
        using var host = new InspectorHost(pid);
        host.Start();

        var act = () => host.Stop();

        act.Should().NotThrow();
        host.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task Stop_WhenAnalyzerCleanupBlocks_ShouldStillReturnPromptly()
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

        var stopTask = Task.Run(() => host.Stop());

        cleanupStarted.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue();
        stopTask.IsCompleted.Should().BeTrue("shutdown should not block on post-stop analyzer cleanup");

        releaseCleanup.Set();
        await stopTask;
    }

    [Fact]
    public async Task Stop_WhenServerTaskLingersAfterCancellation_ShouldStillReturnPromptly()
    {
        var pid = global::WpfDevTools.Tests.Unit.TestHelpers.NextSyntheticProcessId();
        var serverTaskSource = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var host = new InspectorHost(pid);
        SetPrivateField(host, "_serverTask", serverTaskSource.Task);
        SetPrivateField(host, "_cancellationTokenSource", new CancellationTokenSource());
        SetPrivateField(host, "_lifecycleState", 2);
        SetPrivateField(host, "_isRunning", true);

        var stopTask = Task.Run(() => host.Stop());

        await stopTask.WaitAsync(SignalTimeout);

        serverTaskSource.TrySetResult(null);
    }

    [Fact]
    public async Task Start_WhenPreviousStopTimedOutWaitingForServerTask_ShouldRemainBlockedUntilTaskExits()
    {
        var pid = global::WpfDevTools.Tests.Unit.TestHelpers.NextSyntheticProcessId();
        var serverTaskSource = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var host = new InspectorHost(
            pid,
            $"WpfDevTools_{pid}",
            authManager: null,
            certManager: null,
            FileLogLevel.Warning,
            shutdownTimeout: TimeSpan.FromMilliseconds(100));
        SetPrivateField(host, "_serverTask", serverTaskSource.Task);
        SetPrivateField(host, "_cancellationTokenSource", new CancellationTokenSource());
        SetPrivateField(host, "_lifecycleState", 2);
        SetPrivateField(host, "_isRunning", true);

        await Task.Run(() => host.Stop());

        var timeoutDuration = Stopwatch.StartNew();
        var timedOutRestart = Task.Run(() => host.Start());
        var timeoutException = await Assert.ThrowsAsync<TimeoutException>(() => timedOutRestart);
        timeoutDuration.Stop();
        timeoutException.Message.Should().Contain("Timed out after");
        timeoutDuration.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
        host.IsRunning.Should().BeFalse();

        using var blockedRestartEntered = new ManualResetEventSlim(initialState: false);
        var blockedRestart = RunSignaled(host.Start, blockedRestartEntered);
        blockedRestartEntered.Wait(SignalTimeout).Should().BeTrue();
        blockedRestart.IsCompleted.Should().BeFalse("restart should stay blocked until the lingering pre-stop server task actually exits");

        serverTaskSource.TrySetResult(null);
        await blockedRestart;
        host.IsRunning.Should().BeTrue();
    }

    [Fact]
    public void Stop_WhenServerTaskIsCanceled_ShouldTreatCancellationAsNormalShutdown()
    {
        var pid = global::WpfDevTools.Tests.Unit.TestHelpers.NextSyntheticProcessId();
        using var host = new InspectorHost(pid);

        var canceledTask = Task.FromCanceled(new CancellationToken(canceled: true));
        SetPrivateField(host, "_serverTask", canceledTask);
        SetPrivateField(host, "_cancellationTokenSource", new CancellationTokenSource());
        SetPrivateField(host, "_lifecycleState", 2);
        SetPrivateField(host, "_isRunning", true);

        var act = () => host.Stop();

        act.Should().NotThrow("shutdown should treat a canceled server loop as a normal stop path");
        host.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task Start_ConcurrentCallsDuringStartup_ShouldLaunchSingleServerLoop()
    {
        var pid = global::WpfDevTools.Tests.Unit.TestHelpers.NextSyntheticProcessId();
        var gate = new ManualResetEventSlim(initialState: false);
        var firstFactoryEntered = new ManualResetEventSlim(initialState: false);
        var pipeFactoryCalls = 0;

        using var host = new InspectorHost(
            pid,
            $"WpfDevTools_{pid}",
            authManager: null,
            certManager: null,
            FileLogLevel.Warning,
            pipeServerFactory: () =>
            {
                Interlocked.Increment(ref pipeFactoryCalls);
                firstFactoryEntered.Set();
                gate.Wait(TimeSpan.FromSeconds(5));
                return new NamedPipeServerStream(
                    $"WpfDevTools_{pid}",
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
            },
            startupTimeout: TimeSpan.FromSeconds(2));

        var firstStart = Task.Run(() => host.Start());

        firstFactoryEntered.Wait(SignalTimeout)
            .Should().BeTrue("the first startup should enter pipe creation before the second caller runs");

        using var secondStartEntered = new ManualResetEventSlim(initialState: false);
        var secondStart = RunSignaled(host.Start, secondStartEntered);
        secondStartEntered.Wait(SignalTimeout).Should().BeTrue();

        secondStart.IsCompleted.Should().BeFalse("a concurrent Start() call should wait on the same startup readiness result");
        Volatile.Read(ref pipeFactoryCalls).Should().Be(1);

        gate.Set();
        await firstStart;
        await secondStart;

        host.IsRunning.Should().BeTrue();
    }

    [Fact]
    public async Task Start_ConcurrentFailureDuringStartup_ShouldPropagateSameFailure()
    {
        var pid = global::WpfDevTools.Tests.Unit.TestHelpers.NextSyntheticProcessId();
        var gate = new ManualResetEventSlim(initialState: false);
        var firstFactoryEntered = new ManualResetEventSlim(initialState: false);
        var pipeFactoryCalls = 0;

        using var host = new InspectorHost(
            pid,
            $"WpfDevTools_{pid}",
            authManager: null,
            certManager: null,
            FileLogLevel.Warning,
            pipeServerFactory: () =>
            {
                Interlocked.Increment(ref pipeFactoryCalls);
                firstFactoryEntered.Set();
                gate.Wait(TimeSpan.FromSeconds(5));
                throw new IOException("pipe create failed");
            },
            startupTimeout: TimeSpan.FromSeconds(2));

        var firstStart = Task.Run(() => host.Start());

        firstFactoryEntered.Wait(SignalTimeout).Should().BeTrue();

        using var secondStartEntered = new ManualResetEventSlim(initialState: false);
        var secondStart = RunSignaled(host.Start, secondStartEntered);
        secondStartEntered.Wait(SignalTimeout).Should().BeTrue();
        secondStart.IsCompleted.Should().BeFalse();

        gate.Set();

        var firstException = await Assert.ThrowsAsync<IOException>(() => firstStart);
        var secondException = await Assert.ThrowsAsync<IOException>(() => secondStart);

        firstException.Message.Should().Contain("pipe create failed");
        secondException.Message.Should().Contain("pipe create failed");
        host.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task Stop_WhenBlockedPipeCreationResumesAfterTimeout_ShouldNotPublishStalePipe()
    {
        var pid = global::WpfDevTools.Tests.Unit.TestHelpers.NextSyntheticProcessId();
        var secondFactoryEntered = new ManualResetEventSlim(initialState: false);
        var releaseSecondFactory = new ManualResetEventSlim(initialState: false);
        var pipeFactoryCalls = 0;

        using var host = new InspectorHost(
            pid,
            $"WpfDevTools_{pid}",
            authManager: null,
            certManager: null,
            FileLogLevel.Warning,
            pipeServerFactory: () =>
            {
                var call = Interlocked.Increment(ref pipeFactoryCalls);
                if (call == 1)
                {
                    return new NamedPipeServerStream(
                        $"WpfDevTools_{pid}",
                        PipeDirection.InOut,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);
                }

                secondFactoryEntered.Set();
                releaseSecondFactory.Wait(TimeSpan.FromSeconds(10));
                return new NamedPipeServerStream(
                    $"WpfDevTools_{pid}",
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
            },
            startupTimeout: TimeSpan.FromSeconds(2));

        host.Start();

        using (var client = await ConnectToHostAsync(pid))
        {
        }

        secondFactoryEntered.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();

        host.Stop();
        host.IsRunning.Should().BeFalse();

        releaseSecondFactory.Set();

        using var retryClient = new NamedPipeClientStream(
            ".",
            $"WpfDevTools_{pid}",
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        Func<Task> act = async () => await retryClient.ConnectAsync(250);
        await act.Should().ThrowAsync<TimeoutException>();
    }

    [Fact]
    public async Task Start_WhenStopFinalizationWaitsOnStartupFailureCleanup_ShouldDelayRestartUntilCleanupCompletes()
    {
        var pid = global::WpfDevTools.Tests.Unit.TestHelpers.NextSyntheticProcessId();
        var firstFactoryEntered = new ManualResetEventSlim(initialState: false);
        var releaseFirstFactory = new ManualResetEventSlim(initialState: false);
        var pipeFactoryCalls = 0;
        var startupTimeout = TimeSpan.FromMilliseconds(200);

        using var host = new InspectorHost(
            pid,
            $"WpfDevTools_{pid}",
            authManager: null,
            certManager: null,
            FileLogLevel.Warning,
            pipeServerFactory: () =>
            {
                var call = Interlocked.Increment(ref pipeFactoryCalls);
                if (call == 1)
                {
                    firstFactoryEntered.Set();
                    releaseFirstFactory.Wait(TimeSpan.FromSeconds(10));
                    throw new IOException("initial pipe create failed");
                }

                return new NamedPipeServerStream(
                    $"WpfDevTools_{pid}",
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
            },
            startupTimeout: startupTimeout);

        var firstStart = Task.Run(() => host.Start());

        firstFactoryEntered.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue();

        using var secondStartEntered = new ManualResetEventSlim(initialState: false);
        var secondStart = RunSignaled(host.Start, secondStartEntered);
        secondStartEntered.Wait(SignalTimeout).Should().BeTrue();
        secondStart.IsCompleted.Should().BeFalse();
        GetPrivateField<CancellationTokenSource>(host, "_cancellationTokenSource")
            .Token.WaitHandle.WaitOne(SignalTimeout).Should().BeTrue(
                "the startup timeout should cancel its generation before stop finalization is tested");
        firstStart.IsCompleted.Should().BeFalse("startup failure cleanup should still be waiting for the blocked pipe factory");
        secondStart.IsCompleted.Should().BeFalse("the concurrent starter should still be waiting for startup failure cleanup");

        var stopTask = Task.Run(() => host.Stop());
        await stopTask;
        host.IsRunning.Should().BeFalse();

        using var restartEntered = new ManualResetEventSlim(initialState: false);
        var restartTask = RunSignaled(host.Start, restartEntered);
        restartEntered.Wait(SignalTimeout).Should().BeTrue();
        restartTask.IsCompleted.Should().BeFalse("restart should wait for the prior stop finalization while startup-failure cleanup is still pending");

        releaseFirstFactory.Set();

        var firstException = await Assert.ThrowsAsync<TimeoutException>(() => firstStart);
        var secondException = await Assert.ThrowsAsync<TimeoutException>(() => secondStart);

        firstException.Message.Should().Contain("Timed out after");
        secondException.Message.Should().Contain("Timed out after");

        await restartTask;
        host.IsRunning.Should().BeTrue();

        using var client = await ConnectToHostAsync(pid);
    }

    [Fact]
    public async Task Start_WhenPipeCreationCompletesAfterStartupTimeout_ShouldStillFail()
    {
        var pid = global::WpfDevTools.Tests.Unit.TestHelpers.NextSyntheticProcessId();
        var pipeFactoryEntered = new ManualResetEventSlim(initialState: false);
        var releasePipeFactory = new ManualResetEventSlim(initialState: false);

        using var host = new InspectorHost(
            pid,
            $"WpfDevTools_{pid}",
            authManager: null,
            certManager: null,
            FileLogLevel.Warning,
            pipeServerFactory: () =>
            {
                pipeFactoryEntered.Set();
                releasePipeFactory.Wait(TimeSpan.FromSeconds(5));
                return new NamedPipeServerStream(
                    $"WpfDevTools_{pid}",
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
            },
            startupTimeout: TimeSpan.FromMilliseconds(200));

        var startTask = Task.Run(() => host.Start());

        pipeFactoryEntered.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue();
        GetPrivateField<CancellationTokenSource>(host, "_cancellationTokenSource")
            .Token.WaitHandle.WaitOne(SignalTimeout).Should().BeTrue();

        releasePipeFactory.Set();

        var exception = await Assert.ThrowsAsync<TimeoutException>(() => startTask);
        exception.Message.Should().Contain("Timed out after");
        host.IsRunning.Should().BeFalse();

        using var client = new NamedPipeClientStream(
            ".",
            $"WpfDevTools_{pid}",
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        Func<Task> act = async () => await client.ConnectAsync(250);
        await act.Should().ThrowAsync<TimeoutException>();
    }

    [Fact]
    public async Task Start_WhenStopWinsBeforeRunningState_ShouldFail()
    {
        var pid = global::WpfDevTools.Tests.Unit.TestHelpers.NextSyntheticProcessId();
        var beforeStartupCompletionEntered = new ManualResetEventSlim(initialState: false);
        var releaseStartupCompletion = new ManualResetEventSlim(initialState: false);

        using var host = new InspectorHost(
            pid,
            $"WpfDevTools_{pid}",
            authManager: null,
            certManager: null,
            FileLogLevel.Warning,
            beforeStartupCompletion: () =>
            {
                beforeStartupCompletionEntered.Set();
                releaseStartupCompletion.Wait(TimeSpan.FromSeconds(5));
            });

        var startTask = Task.Run(() => host.Start());

        beforeStartupCompletionEntered.Wait(SignalTimeout).Should().BeTrue();

        host.Stop();
        releaseStartupCompletion.Set();

        await Assert.ThrowsAsync<OperationCanceledException>(() => startTask);
        host.IsRunning.Should().BeFalse();
    }

}
