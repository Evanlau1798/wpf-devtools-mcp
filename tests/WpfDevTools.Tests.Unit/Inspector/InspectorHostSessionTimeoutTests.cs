using System.IO.Pipes;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Inspector.Host;
using WpfDevTools.Shared.Enums;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Serialization;
using WpfDevTools.Shared.Utilities;
using Xunit;
using WpfDevTools.Tests.Unit.Execution;

namespace WpfDevTools.Tests.Unit.Inspector;

[Collection("TimingSensitive")]
public sealed class InspectorHostSessionTimeoutTests : IDisposable
{
    private readonly IDisposable _plaintextPolicy = UnsafePlaintextInspectorHostTestEnvironment.BeginScope();

    public void Dispose()
    {
        _plaintextPolicy.Dispose();
    }

    [Fact]
    public async Task IdleConnection_ShouldBeReapedAfterSessionReadTimeout_AndAllowNextClient()
    {
        var pid = global::WpfDevTools.Tests.Unit.TestHelpers.NextSyntheticProcessId();
        using var host = new InspectorHost(
            pid,
            $"WpfDevTools_{pid}",
            authManager: null,
            certManager: null,
            FileLogLevel.Warning,
            startupTimeout: TimeSpan.FromSeconds(2),
            sessionReadTimeout: TimeSpan.FromMilliseconds(250));
        host.Start();

        using var idleClient = new NamedPipeClientStream(
            ".",
            $"WpfDevTools_{pid}",
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        await idleClient.ConnectAsync(5_000);

        await Task.Delay(600);

        using var activeClient = new NamedPipeClientStream(
            ".",
            $"WpfDevTools_{pid}",
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        await activeClient.ConnectAsync(2_000);

        var request = new InspectorRequest
        {
            Id = "idle-timeout-ping",
            Method = "ping",
            Params = null
        };
        await MessageFraming.WriteMessageAsync(activeClient, JsonSerializer.Serialize(request));

        using var readCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var responseJson = await MessageFraming.ReadMessageAsync(activeClient, readCts.Token);
        var response = JsonSerializer.Deserialize<InspectorResponse>(responseJson);

        response.Should().NotBeNull();
        response!.Id.Should().Be("idle-timeout-ping");
        response.Error.Should().BeNull();
    }

    [Fact]
    public async Task PartialFrameConnection_ShouldBeReapedAfterSessionReadTimeout_AndAllowNextClient()
    {
        var pid = global::WpfDevTools.Tests.Unit.TestHelpers.NextSyntheticProcessId();
        using var host = new InspectorHost(
            pid,
            $"WpfDevTools_{pid}",
            authManager: null,
            certManager: null,
            FileLogLevel.Warning,
            startupTimeout: TimeSpan.FromSeconds(2),
            sessionReadTimeout: TimeSpan.FromMilliseconds(250));
        host.Start();

        using var stalledClient = new NamedPipeClientStream(
            ".",
            $"WpfDevTools_{pid}",
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        await stalledClient.ConnectAsync(5_000);
        await SendPartialFrameAsync(stalledClient, declaredPayloadLength: 32, payloadBytes: new byte[] { 0x7B, 0x22 });

        await Task.Delay(600);

        using var activeClient = new NamedPipeClientStream(
            ".",
            $"WpfDevTools_{pid}",
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        await activeClient.ConnectAsync(2_000);

        var request = new InspectorRequest
        {
            Id = "partial-frame-timeout-ping",
            Method = "ping",
            Params = null
        };
        await MessageFraming.WriteMessageAsync(activeClient, JsonSerializer.Serialize(request));

        using var readCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var responseJson = await MessageFraming.ReadMessageAsync(activeClient, readCts.Token);
        var response = JsonSerializer.Deserialize<InspectorResponse>(responseJson);

        response.Should().NotBeNull();
        response!.Id.Should().Be("partial-frame-timeout-ping");
        response.Error.Should().BeNull();
    }

    [Fact]
    public async Task NonCooperativeHandler_ShouldReturnTimeoutAndStopHostBeforeNextClient()
    {
        var blocker = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pid = global::WpfDevTools.Tests.Unit.TestHelpers.NextSyntheticProcessId();
        using var host = new InspectorHost(
            pid,
            $"WpfDevTools_{pid}",
            authManager: null,
            certManager: null,
            FileLogLevel.Warning,
            startupTimeout: TimeSpan.FromSeconds(2),
            requestTimeout: TimeSpan.FromMilliseconds(250),
            configureDispatcherForTesting: dispatcher =>
                dispatcher.AddSimpleHandlerForTesting("never_complete", (_, _) => blocker.Task),
            sessionReadTimeout: TimeSpan.FromSeconds(5));
        host.Start();

        using var stalledClient = new NamedPipeClientStream(
            ".",
            $"WpfDevTools_{pid}",
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        await stalledClient.ConnectAsync(5_000);

        await MessageFraming.WriteMessageAsync(stalledClient, JsonSerializer.Serialize(new InspectorRequest
        {
            Id = "non-cooperative-timeout",
            Method = "never_complete",
            Params = null
        }));

        using var readCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var timeoutJson = await MessageFraming.ReadMessageAsync(stalledClient, readCts.Token);
        var timeoutResponse = JsonSerializer.Deserialize<InspectorResponse>(timeoutJson);
        timeoutResponse.Should().NotBeNull();
        timeoutResponse!.Id.Should().Be("non-cooperative-timeout");
        timeoutResponse.Error.Should().NotBeNull();
        timeoutResponse.Error!.Code.Should().Be(ErrorCode.Timeout);

        using var nextClient = new NamedPipeClientStream(
            ".",
            $"WpfDevTools_{pid}",
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        await Assert.ThrowsAnyAsync<Exception>(() => nextClient.ConnectAsync(500));
        host.IsRunning.Should().BeFalse("hard timeout recovery must stop accepting new work while the timed-out handler may still run");

        blocker.TrySetResult(new { success = true });
    }

    private static async Task SendPartialFrameAsync(NamedPipeClientStream client, int declaredPayloadLength, byte[] payloadBytes)
    {
        var lengthPrefix = BitConverter.GetBytes(declaredPayloadLength);
        await client.WriteAsync(lengthPrefix, 0, lengthPrefix.Length);
        await client.WriteAsync(payloadBytes, 0, payloadBytes.Length);
        await client.FlushAsync();
    }
}
