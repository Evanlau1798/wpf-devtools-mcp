using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Xunit;
using FluentAssertions;
using WpfDevTools.Inspector.Host;
using WpfDevTools.Shared.Serialization;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Enums;

namespace WpfDevTools.Tests.Unit.Inspector;

/// <summary>
/// Unit tests for InspectorHost Named Pipe server lifecycle and request/response handling.
/// Each test that needs a running host creates and disposes its own instance to avoid
/// pipe-name collisions; the class-level _host is used only for pure lifecycle tests.
/// </summary>
public class InspectorHostTests : IDisposable
{
    // Use a random process-id range that will never collide with real PIDs
    // (kernel reserves 0-4; user range is up to ~4 million on 64-bit Windows, but
    //  our random int range 100_000-999_999 is far from any fixture's real PID)
    private readonly int _testProcessId;
    private readonly InspectorHost _host;

    public InspectorHostTests()
    {
        _testProcessId = global::WpfDevTools.Tests.Unit.TestHelpers.NextSyntheticProcessId();
        _host = new InspectorHost(_testProcessId);
    }

    public void Dispose()
    {
        _host.Dispose();
    }

    // ── Lifecycle: initial state ──────────────────────────────────────────────

    [Fact]
    public void Constructor_ShouldSetIsRunningFalse()
    {
        // The host is freshly created; it must not be running yet
        _host.IsRunning.Should().BeFalse();
    }

    // ── Lifecycle: Start ──────────────────────────────────────────────────────

    [Fact]
    public void Start_ShouldSetIsRunningTrue()
    {
        _host.Start();

        _host.IsRunning.Should().BeTrue();
    }

    [Fact]
    public void Start_WhenAlreadyRunning_ShouldNotThrow()
    {
        _host.Start();

        // Second call must be idempotent – must not throw
        var act = () => _host.Start();
        act.Should().NotThrow();

        _host.IsRunning.Should().BeTrue();
    }

    // ── Lifecycle: Stop ───────────────────────────────────────────────────────

    [Fact]
    public void Stop_ShouldSetIsRunningFalse()
    {
        _host.Start();
        _host.IsRunning.Should().BeTrue(); // pre-condition

        _host.Stop();

        _host.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void Stop_WhenNotRunning_ShouldNotThrow()
    {
        _host.IsRunning.Should().BeFalse(); // pre-condition

        // Calling Stop on a host that was never started must be idempotent
        var act = () => _host.Stop();
        act.Should().NotThrow();
    }

    // ── Lifecycle: Dispose ────────────────────────────────────────────────────

    [Fact]
    public void Dispose_ShouldStopServer()
    {
        // Use a fresh host so _host is not double-disposed by the class Dispose()
        var pid = global::WpfDevTools.Tests.Unit.TestHelpers.NextSyntheticProcessId();
        var localHost = new InspectorHost(pid);
        localHost.Start();
        localHost.IsRunning.Should().BeTrue(); // pre-condition

        localHost.Dispose();

        localHost.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void Dispose_WhenNotStarted_ShouldNotThrow()
    {
        var pid = global::WpfDevTools.Tests.Unit.TestHelpers.NextSyntheticProcessId();
        var localHost = new InspectorHost(pid);
        localHost.IsRunning.Should().BeFalse(); // pre-condition

        var act = () => localHost.Dispose();
        act.Should().NotThrow();
    }

    // ── Roundtrip: ping ───────────────────────────────────────────────────────

    [Fact]
    public async Task PingRequest_ShouldReturnPongResponse()
    {
        var pid = global::WpfDevTools.Tests.Unit.TestHelpers.NextSyntheticProcessId();
        using var host = new InspectorHost(pid);
        host.Start();

        using var client = new NamedPipeClientStream(
            ".",
            $"WpfDevTools_{pid}",
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        // The server loop starts in a background Task; give it a moment to call
        // WaitForConnectionAsync before we connect (5 s budget is more than enough)
        await client.ConnectAsync(5_000);

        var request = new InspectorRequest
        {
            Id = "ping-1",
            Method = "ping",
            Params = null
        };

        var requestJson = JsonSerializer.Serialize(request);
        await MessageFraming.WriteMessageAsync(client, requestJson);

        using var readCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var responseJson = await MessageFraming.ReadMessageAsync(client, readCts.Token);
        var response = JsonSerializer.Deserialize<InspectorResponse>(responseJson);

        response.Should().NotBeNull();
        response!.Id.Should().Be("ping-1");
        response.Error.Should().BeNull();
        response.Result.Should().NotBeNull();

        var result = response.Result!.Value;
        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("status").GetString().Should().Be("pong");
    }

    // ── Roundtrip: unknown method → MethodNotFound ────────────────────────────

    [Fact]
    public async Task UnknownMethod_ShouldReturnMethodNotFoundError()
    {
        var pid = global::WpfDevTools.Tests.Unit.TestHelpers.NextSyntheticProcessId();
        using var host = new InspectorHost(pid);
        host.Start();

        using var client = new NamedPipeClientStream(
            ".",
            $"WpfDevTools_{pid}",
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        await client.ConnectAsync(5_000);

        var request = new InspectorRequest
        {
            Id = "unknown-1",
            Method = "this_method_does_not_exist",
            Params = null
        };

        var requestJson = JsonSerializer.Serialize(request);
        await MessageFraming.WriteMessageAsync(client, requestJson);

        using var readCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var responseJson = await MessageFraming.ReadMessageAsync(client, readCts.Token);
        var response = JsonSerializer.Deserialize<InspectorResponse>(responseJson);

        response.Should().NotBeNull();
        response!.Id.Should().Be("unknown-1");
        response.Result.Should().BeNull();
        response.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be(ErrorCode.MethodNotFound);
        response.Error.Message.Should().Contain("this_method_does_not_exist");
    }

    // ── Roundtrip: null JSON → error response ────────────────────────────────
    //
    // Sending the JSON literal "null" is valid JSON that deserializes to a null
    // InspectorRequest reference, which triggers the explicit null-check branch in
    // HandleClientAsync and causes the server to send an error response with id "unknown".
    // (A completely malformed JSON string throws JsonException, which the server catches
    //  internally without sending a response, so we use the null-literal path here.)

    [Fact]
    public async Task InvalidRequest_NullJson_ShouldReturnError()
    {
        var pid = global::WpfDevTools.Tests.Unit.TestHelpers.NextSyntheticProcessId();
        using var host = new InspectorHost(pid);
        host.Start();

        using var client = new NamedPipeClientStream(
            ".",
            $"WpfDevTools_{pid}",
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        await client.ConnectAsync(5_000);

        // "null" is valid JSON but deserializes InspectorRequest to null,
        // which triggers the server's null-request error path
        await MessageFraming.WriteMessageAsync(client, "null");

        using var readCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var responseJson = await MessageFraming.ReadMessageAsync(client, readCts.Token);
        var response = JsonSerializer.Deserialize<InspectorResponse>(responseJson);

        // The server gracefully returns an error response rather than crashing
        response.Should().NotBeNull();
        response!.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be(ErrorCode.InvalidRequest);
        // The server uses "unknown" as the id when the request cannot be parsed
        response.Id.Should().Be("unknown");
    }

    // ── Roundtrip: multiple sequential requests on one connection ─────────────

    [Fact]
    public async Task MultipleRequests_OnSameConnection_ShouldEachReceiveCorrectResponse()
    {
        var pid = global::WpfDevTools.Tests.Unit.TestHelpers.NextSyntheticProcessId();
        using var host = new InspectorHost(pid);
        host.Start();

        using var client = new NamedPipeClientStream(
            ".",
            $"WpfDevTools_{pid}",
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        await client.ConnectAsync(5_000);

        using var readCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        // First request: ping
        var pingRequest = new InspectorRequest { Id = "multi-1", Method = "ping", Params = null };
        await MessageFraming.WriteMessageAsync(client, JsonSerializer.Serialize(pingRequest));
        var pingJson = await MessageFraming.ReadMessageAsync(client, readCts.Token);
        var pingResponse = JsonSerializer.Deserialize<InspectorResponse>(pingJson);

        pingResponse.Should().NotBeNull();
        pingResponse!.Id.Should().Be("multi-1");
        pingResponse.Error.Should().BeNull();

        // Second request: unknown method
        var unknownRequest = new InspectorRequest { Id = "multi-2", Method = "no_such_method", Params = null };
        await MessageFraming.WriteMessageAsync(client, JsonSerializer.Serialize(unknownRequest));
        var unknownJson = await MessageFraming.ReadMessageAsync(client, readCts.Token);
        var unknownResponse = JsonSerializer.Deserialize<InspectorResponse>(unknownJson);

        unknownResponse.Should().NotBeNull();
        unknownResponse!.Id.Should().Be("multi-2");
        unknownResponse.Error.Should().NotBeNull();
        unknownResponse.Error!.Code.Should().Be(ErrorCode.MethodNotFound);
    }

    // ── Roundtrip: response id matches request id ────────────────────────────

    [Fact]
    public async Task Response_IdShouldAlwaysMatchRequestId()
    {
        var pid = global::WpfDevTools.Tests.Unit.TestHelpers.NextSyntheticProcessId();
        using var host = new InspectorHost(pid);
        host.Start();

        using var client = new NamedPipeClientStream(
            ".",
            $"WpfDevTools_{pid}",
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        await client.ConnectAsync(5_000);

        const string expectedId = "correlation-id-abc-123";
        var request = new InspectorRequest
        {
            Id = expectedId,
            Method = "ping",
            Params = null
        };

        await MessageFraming.WriteMessageAsync(client, JsonSerializer.Serialize(request));

        using var readCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var responseJson = await MessageFraming.ReadMessageAsync(client, readCts.Token);
        var response = JsonSerializer.Deserialize<InspectorResponse>(responseJson);

        response.Should().NotBeNull();
        response!.Id.Should().Be(expectedId,
            "the response id must always mirror the request id for correlation");
    }

    // ── Pipe naming convention ────────────────────────────────────────────────

    [Fact]
    public async Task PipeName_ShouldFollowWpfDevToolsConvention()
    {
        // Verify that the pipe is actually reachable under the conventional name
        var pid = global::WpfDevTools.Tests.Unit.TestHelpers.NextSyntheticProcessId();
        using var host = new InspectorHost(pid);
        host.Start();

        var expectedPipeName = $"WpfDevTools_{pid}";

        using var client = new NamedPipeClientStream(
            ".",
            expectedPipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        // If the pipe name is wrong this will throw a TimeoutException
        var connectAct = async () => await client.ConnectAsync(5_000);
        await connectAct.Should().NotThrowAsync(
            $"the pipe must be reachable under the name '{expectedPipeName}'");
    }
}
