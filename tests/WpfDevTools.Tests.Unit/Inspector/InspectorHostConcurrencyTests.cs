using System.IO.Pipes;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
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
public class InspectorHostConcurrencyTests
{
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

        var completed = await Task.WhenAny(stopTask, Task.Delay(10_000)) == stopTask;
        completed.Should().BeTrue("Stop() should complete within reasonable time");

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

        SpinWait.SpinUntil(() => Volatile.Read(ref pipeFactoryCalls) == 1, 1_000)
            .Should().BeTrue("the first startup should enter pipe creation before the second caller runs");

        var secondStart = Task.Run(() => host.Start());
        await Task.Delay(100);

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
                gate.Wait(TimeSpan.FromSeconds(5));
                throw new IOException("pipe create failed");
            },
            startupTimeout: TimeSpan.FromSeconds(2));

        var firstStart = Task.Run(() => host.Start());

        SpinWait.SpinUntil(() => Volatile.Read(ref pipeFactoryCalls) == 1, 1_000)
            .Should().BeTrue();

        var secondStart = Task.Run(() => host.Start());
        await Task.Delay(100);
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
        await Task.Delay(250);

        using var retryClient = new NamedPipeClientStream(
            ".",
            $"WpfDevTools_{pid}",
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        Func<Task> act = async () => await retryClient.ConnectAsync(250);
        await act.Should().ThrowAsync<TimeoutException>();
    }

    [Fact]
    public async Task Start_WhenStopTimesOutDuringStartupFailureCleanup_ShouldNotClearRestartedHostState()
    {
        var pid = global::WpfDevTools.Tests.Unit.TestHelpers.NextSyntheticProcessId();
        var firstFactoryEntered = new ManualResetEventSlim(initialState: false);
        var releaseFirstFactory = new ManualResetEventSlim(initialState: false);
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
            startupTimeout: TimeSpan.FromMilliseconds(200));

        var firstStart = Task.Run(() => host.Start());

        firstFactoryEntered.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue();

        var secondStart = Task.Run(() => host.Start());
        await Task.Delay(100);
        secondStart.IsCompleted.Should().BeFalse();

        var stopTask = Task.Run(() => host.Stop());
        await stopTask;
        host.IsRunning.Should().BeFalse();

        await Task.Run(() => host.Start());
        host.IsRunning.Should().BeTrue();

        releaseFirstFactory.Set();

        var firstException = await Assert.ThrowsAsync<TimeoutException>(() => firstStart);
        var secondException = await Assert.ThrowsAsync<TimeoutException>(() => secondStart);

        firstException.Message.Should().Contain("Timed out after");
        secondException.Message.Should().Contain("Timed out after");
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
        await Task.Delay(300);

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

        beforeStartupCompletionEntered.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue();

        var concurrentStartTask = Task.Run(() => host.Start());
        await Task.Delay(100);
        concurrentStartTask.IsCompleted.Should().BeFalse();

        host.Stop();
        concurrentStartTask.IsCompleted.Should().BeFalse("the shared startup result should not complete until rollback has finished");
        releaseStartupCompletion.Set();

        await Assert.ThrowsAsync<OperationCanceledException>(() => startTask);
        await Assert.ThrowsAsync<OperationCanceledException>(() => concurrentStartTask);
        host.IsRunning.Should().BeFalse();
    }

    private static void SetPrivateField<T>(InspectorHost host, string fieldName, T value)
    {
        var field = typeof(InspectorHost).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        field!.SetValue(host, value);
    }

    private static async Task<NamedPipeClientStream> ConnectToHostAsync(int processId)
    {
        const int maxAttempts = 5;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var client = new NamedPipeClientStream(
                ".",
                $"WpfDevTools_{processId}",
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

            try
            {
                await client.ConnectAsync(1_000);
                return client;
            }
            catch (TimeoutException) when (attempt < maxAttempts)
            {
                client.Dispose();
                await Task.Delay(100);
            }
        }

        throw new TimeoutException($"Timed out waiting for InspectorHost pipe for synthetic process {processId}.");
    }
}
