using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Inspector.Host;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Shared.Security;
using WpfDevTools.Shared.Serialization;
using WpfDevTools.Tests.Unit.Execution;
using Xunit;

namespace WpfDevTools.Tests.Unit.McpServer;

[Collection("TimingSensitive")]
public class NamedPipeClientTimeoutBudgetTests
{
    [Fact]
    public async Task ConnectAsync_WithRetries_ShouldRespectTotalTimeoutBudget()
    {
        using var client = new NamedPipeClient(global::WpfDevTools.Tests.Unit.TestHelpers.NextSyntheticProcessId(), $"WpfDevTools_Test_{Guid.NewGuid():N}");
        var timeout = TimeSpan.FromMilliseconds(150);

        var sw = Stopwatch.StartNew();
        var connected = await client.ConnectAsync(timeout, maxRetries: 3);
        sw.Stop();

        connected.Should().BeFalse();
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(1000),
            "retry attempts must share the same timeout budget instead of spending the full timeout on each attempt");
    }

    [Fact]
    public async Task ConnectAsync_WithAuthenticatedClientAndPlaintextInspectorHost_ShouldReturnTimeout()
    {
        var pid = global::WpfDevTools.Tests.Unit.TestHelpers.NextSyntheticProcessId();
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var authManager = new AuthenticationManager(() => secret);

        using var host = new InspectorHost(pid);
        host.Start();

        using var client = new NamedPipeClient(pid, authManager);

        var connected = await client.ConnectAsync(TimeSpan.FromMilliseconds(300));

        connected.Should().BeFalse();
        client.LastConnectFailure.Should().Be(NamedPipeConnectFailure.Timeout);
    }

    [Fact]
    public async Task ConnectAsync_WithAuthenticatedClientAndSilentAuthPipe_ShouldRespectExplicitAuthTimeoutBudget()
    {
        var pid = global::WpfDevTools.Tests.Unit.TestHelpers.NextSyntheticProcessId();
        var pipeName = $"WpfDevTools_Test_{Guid.NewGuid():N}";
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var authManager = new AuthenticationManager(() => secret);
        using var serverLifetime = new CancellationTokenSource();
        using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync(serverLifetime.Token);
            await Task.Delay(Timeout.InfiniteTimeSpan, serverLifetime.Token);
        });

        using var client = new NamedPipeClient(
            pid,
            pipeName,
            authManager,
            certManager: null,
            enforceHostCompatibilityValidation: false);

        var sw = Stopwatch.StartNew();
        var connected = await client.ConnectAsync(TimeSpan.FromMilliseconds(150), maxRetries: 1);
        sw.Stop();

        serverLifetime.Cancel();
        await serverTask.IgnoreExceptionsAsync();

        connected.Should().BeFalse();
        client.LastConnectFailure.Should().Be(NamedPipeConnectFailure.Timeout);
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(800),
            "auth read/write phases must be guarded by an explicit budget even when the underlying pipe operation ignores cancellation");
    }

    [Fact]
    public async Task ConnectAsync_WithAuthenticatedClientAndSilentAuthPipe_WhenCallerCancels_ShouldPropagateCancellation()
    {
        var pid = global::WpfDevTools.Tests.Unit.TestHelpers.NextSyntheticProcessId();
        var pipeName = $"WpfDevTools_Test_{Guid.NewGuid():N}";
        var authManager = new AuthenticationManager(() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        using var serverLifetime = new CancellationTokenSource();
        using var callerCancellation = new CancellationTokenSource();
        using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        var serverConnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var serverTask = RunSilentPipeServerAsync(server, serverConnected, serverLifetime.Token);
        using var client = new NamedPipeClient(
            pid,
            pipeName,
            authManager,
            certManager: null,
            enforceHostCompatibilityValidation: false);

        var connectTask = client.ConnectAsync(
            TimeSpan.FromSeconds(10),
            maxRetries: 1,
            cancellationToken: callerCancellation.Token);

        try
        {
            await serverConnected.Task.WaitAsync(TimeSpan.FromSeconds(2));
            callerCancellation.Cancel();

            await connectTask.Invoking(static task => task).Should().ThrowAsync<OperationCanceledException>();
        }
        finally
        {
            serverLifetime.Cancel();
            await serverTask.IgnoreExceptionsAsync();
        }
    }

    [Fact]
    public async Task ConnectAsync_WithTlsClientAndSilentPipe_WhenCallerCancels_ShouldPropagateCancellation()
    {
        var pid = global::WpfDevTools.Tests.Unit.TestHelpers.NextSyntheticProcessId();
        var pipeName = $"WpfDevTools_Test_{Guid.NewGuid():N}";
        var certDirectory = Path.Combine(Path.GetTempPath(), "WpfDevTools_Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(certDirectory);
        using var serverLifetime = new CancellationTokenSource();
        using var callerCancellation = new CancellationTokenSource();
        using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        var serverConnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var serverTask = RunSilentPipeServerAsync(server, serverConnected, serverLifetime.Token);

        try
        {
            var certManager = new CertificateManager(certDirectory);
            using var client = new NamedPipeClient(
                pid,
                pipeName,
                authManager: null,
                certManager,
                enforceHostCompatibilityValidation: false);

            var connectTask = client.ConnectAsync(
                TimeSpan.FromSeconds(10),
                maxRetries: 1,
                cancellationToken: callerCancellation.Token);
            await serverConnected.Task.WaitAsync(TimeSpan.FromSeconds(2));
            callerCancellation.Cancel();

            await connectTask.Invoking(static task => task).Should().ThrowAsync<OperationCanceledException>();
        }
        finally
        {
            serverLifetime.Cancel();
            await serverTask.IgnoreExceptionsAsync();
            try { Directory.Delete(certDirectory, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task ConnectAsync_WithHostValidationPending_WhenCallerCancels_ShouldPropagateCancellation()
    {
        var pid = Environment.ProcessId;
        var pipeName = $"WpfDevTools_Test_{Guid.NewGuid():N}";
        using var serverLifetime = new CancellationTokenSource();
        using var callerCancellation = new CancellationTokenSource();
        using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        var validationRequestReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync(serverLifetime.Token);
            var requestJson = await MessageFraming.ReadMessageAsync(server, serverLifetime.Token);
            using var requestDocument = JsonDocument.Parse(requestJson);
            var request = requestDocument.RootElement;

            request.GetProperty("method").GetString().Should().Be("ping");
            request.GetProperty("id").GetString().Should().StartWith("connect-verify-");
            validationRequestReceived.SetResult();

            await Task.Delay(Timeout.InfiniteTimeSpan, serverLifetime.Token);
        });

        using var client = new NamedPipeClient(
            pid,
            pipeName,
            authManager: null,
            certManager: null,
            enforceHostCompatibilityValidation: true);

        var connectTask = client.ConnectAsync(
            TimeSpan.FromSeconds(10),
            maxRetries: 1,
            cancellationToken: callerCancellation.Token);

        try
        {
            await validationRequestReceived.Task.WaitAsync(TimeSpan.FromSeconds(2));
            callerCancellation.Cancel();

            await connectTask.Invoking(static task => task).Should().ThrowAsync<OperationCanceledException>();
        }
        finally
        {
            serverLifetime.Cancel();
            await serverTask.IgnoreExceptionsAsync();
        }
    }

    [Fact]
    public async Task ConnectPhaseTimeoutGuard_WhenNet48StyleOperationIgnoresCancellation_ShouldStopAtTheBudget()
    {
        var neverCompletes = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var sw = Stopwatch.StartNew();
        Func<Task> act = () => NamedPipeClient.WaitForConnectPhaseAsync(neverCompletes.Task, timeout.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        sw.Stop();

        sw.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(800),
            "NET48-style AuthenticateAsClientAsync/read/write tasks do not provide reliable cancellation overloads");
    }

    [Fact]
    public async Task Dispose_WithInFlightRequest_ShouldCompleteBeforeRequestTimeout()
    {
        var pid = global::WpfDevTools.Tests.Unit.TestHelpers.NextSyntheticProcessId();
        var pipeName = $"WpfDevTools_Test_{Guid.NewGuid():N}";
        using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        using var client = new NamedPipeClient(
            pid,
            pipeName,
            authManager: null,
            certManager: null,
            enforceHostCompatibilityValidation: false,
            requestTimeout: TimeSpan.FromSeconds(10));
        using var requestLifetime = new CancellationTokenSource();
        var requestObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var serverTask = Task.Run(async () =>
        {
            try
            {
                await server.WaitForConnectionAsync(requestLifetime.Token);
                await MessageFraming.ReadMessageAsync(server, requestLifetime.Token);
                requestObserved.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, requestLifetime.Token);
            }
            catch (OperationCanceledException)
            {
                requestObserved.TrySetCanceled(requestLifetime.Token);
            }
            catch (Exception ex)
            {
                requestObserved.TrySetException(ex);
            }
        });

        var connected = await client.ConnectAsync(TimeSpan.FromSeconds(2), maxRetries: 1);
        connected.Should().BeTrue();

        var sendTask = client.SendRequestAsync("ping", "dispose-timeout-test", new { }, requestLifetime.Token);
        await requestObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var disposeTask = Task.Run(client.Dispose);
        var completedBeforeTimeout = await Task.WhenAny(
            disposeTask,
            Task.Delay(TimeSpan.FromSeconds(2))) == disposeTask;

        requestLifetime.Cancel();
        server.Dispose();
        await serverTask.IgnoreExceptionsAsync();
        await sendTask.IgnoreExceptionsAsync();
        await disposeTask.WaitAsync(TimeSpan.FromSeconds(2));

        completedBeforeTimeout.Should().BeTrue(
            "Dispose should close the pipe and use a bounded semaphore wait instead of blocking until the request timeout");
    }

    private static async Task RunSilentPipeServerAsync(
        NamedPipeServerStream server,
        TaskCompletionSource serverConnected,
        CancellationToken cancellationToken)
    {
        await server.WaitForConnectionAsync(cancellationToken);
        serverConnected.TrySetResult();
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }
}

internal static class NamedPipeClientTimeoutBudgetTestTaskExtensions
{
    public static async Task IgnoreExceptionsAsync(this Task task)
    {
        try
        {
            await task;
        }
        catch
        {
        }
    }
}
