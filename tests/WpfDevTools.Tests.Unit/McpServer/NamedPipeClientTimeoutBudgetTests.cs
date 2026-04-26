using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
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
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(800),
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
            Task.Delay(TimeSpan.FromMilliseconds(500))) == disposeTask;

        requestLifetime.Cancel();
        server.Dispose();
        await serverTask.IgnoreExceptionsAsync();
        await sendTask.IgnoreExceptionsAsync();
        await disposeTask.WaitAsync(TimeSpan.FromSeconds(2));

        completedBeforeTimeout.Should().BeTrue(
            "Dispose should close the pipe and use a bounded semaphore wait instead of blocking until the request timeout");
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
