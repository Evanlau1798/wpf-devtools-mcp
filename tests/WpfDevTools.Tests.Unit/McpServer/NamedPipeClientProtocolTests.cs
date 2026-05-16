using System.IO.Pipes;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Serialization;

namespace WpfDevTools.Tests.Unit.McpServer;

[Collection("TimingSensitive")]
public class NamedPipeClientProtocolTests
{
    [Fact]
    public async Task SendRequestAsync_WhenResponseIdDoesNotMatch_ShouldThrowInvalidOperationException()
    {
        var processId = global::WpfDevTools.Tests.Unit.TestHelpers.NextSyntheticProcessId();
        var pipeName = $"WpfDevTools_Test_{Guid.NewGuid():N}";

        using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();
            var requestJson = await MessageFraming.ReadMessageAsync(server, CancellationToken.None);
            var request = JsonSerializer.Deserialize<InspectorRequest>(requestJson);

            request.Should().NotBeNull();

            var response = new InspectorResponse
            {
                Id = request!.Id + "-unexpected",
                CorrelationId = request.CorrelationId,
                Result = JsonSerializer.SerializeToElement(new { success = true }),
                Error = null
            };

            await MessageFraming.WriteMessageAsync(
                server,
                JsonSerializer.Serialize(response),
                CancellationToken.None);
        });

        using var client = new NamedPipeClient(processId, pipeName);
        (await client.ConnectAsync(TimeSpan.FromSeconds(5), maxRetries: 1)).Should().BeTrue();

        var act = async () => await client.SendRequestAsync(
            "ping",
            "expected-id",
            new { },
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*response id*");

        await serverTask;
    }

    [Fact]
    public async Task SendRequestAsync_WhenCancelledWhileAwaitingResponse_ShouldResetConnection()
    {
        var processId = global::WpfDevTools.Tests.Unit.TestHelpers.NextSyntheticProcessId();
        var pipeName = $"WpfDevTools_Test_{Guid.NewGuid():N}";
        var requestReceived = new TaskCompletionSource<InspectorRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowServerCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();
            var requestJson = await MessageFraming.ReadMessageAsync(server, CancellationToken.None);
            var request = JsonSerializer.Deserialize<InspectorRequest>(requestJson);

            request.Should().NotBeNull();
            requestReceived.SetResult(request!);

            await allowServerCompletion.Task;

            try
            {
                var response = new InspectorResponse
                {
                    Id = request!.Id,
                    CorrelationId = request.CorrelationId,
                    Result = JsonSerializer.SerializeToElement(new { success = true }),
                    Error = null
                };

                await MessageFraming.WriteMessageAsync(
                    server,
                    JsonSerializer.Serialize(response),
                    CancellationToken.None);
            }
            catch (IOException)
            {
                // Client cancellation resets the pipe, so the server may observe a broken stream.
            }
        });

        using var client = new NamedPipeClient(processId, pipeName);
        (await client.ConnectAsync(TimeSpan.FromSeconds(5), maxRetries: 1)).Should().BeTrue();

        using var cts = new CancellationTokenSource();
        var sendTask = client.SendRequestAsync(
            "ping",
            "expected-id",
            new { },
            cts.Token);

        await requestReceived.Task;
        cts.Cancel();

        await FluentActions.Invoking(async () => await sendTask)
            .Should().ThrowAsync<OperationCanceledException>();

        client.IsConnected.Should().BeFalse(
            "cancelling an in-flight pipe read must reset the client so late responses cannot corrupt the next request");

        allowServerCompletion.SetResult();
        await serverTask;
    }

    [Fact]
    public async Task SendRequestAsync_WhenResponseDoesNotArriveWithinRequestTimeout_ShouldResetConnectionAndThrowTimeoutException()
    {
        var processId = global::WpfDevTools.Tests.Unit.TestHelpers.NextSyntheticProcessId();
        var pipeName = $"WpfDevTools_Test_{Guid.NewGuid():N}";
        var requestReceived = new TaskCompletionSource<InspectorRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowServerCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();
            var requestJson = await MessageFraming.ReadMessageAsync(server, CancellationToken.None);
            var request = JsonSerializer.Deserialize<InspectorRequest>(requestJson);

            request.Should().NotBeNull();
            requestReceived.SetResult(request!);

            await allowServerCompletion.Task;

            try
            {
                var response = new InspectorResponse
                {
                    Id = request!.Id,
                    CorrelationId = request.CorrelationId,
                    Result = JsonSerializer.SerializeToElement(new { success = true }),
                    Error = null
                };

                await MessageFraming.WriteMessageAsync(
                    server,
                    JsonSerializer.Serialize(response),
                    CancellationToken.None);
            }
            catch (IOException)
            {
                // Client timeout resets the pipe before the delayed response is written.
            }
        });

        using var client = new NamedPipeClient(
            processId,
            pipeName,
            authManager: null,
            certManager: null,
            enforceHostCompatibilityValidation: false,
            requestTimeout: TimeSpan.FromMilliseconds(100));
        (await client.ConnectAsync(TimeSpan.FromSeconds(5), maxRetries: 1)).Should().BeTrue();

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var sendTask = client.SendRequestAsync(
            "ping",
            "timeout-id",
            new { },
            CancellationToken.None);

        await requestReceived.Task;

        await FluentActions.Invoking(async () => await sendTask)
            .Should().ThrowAsync<TimeoutException>();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
        client.IsConnected.Should().BeFalse(
            "timing out an in-flight request must reset the client so the next request cannot reuse a stale secure transport");

        allowServerCompletion.SetResult();
        await serverTask;
    }

    [Fact]
    public async Task SendRequestAsync_WhenTimedOutWaitingForPipeSemaphore_ShouldNotResetConnection()
    {
        var processId = global::WpfDevTools.Tests.Unit.TestHelpers.NextSyntheticProcessId();
        var pipeName = $"WpfDevTools_Test_{Guid.NewGuid():N}";

        using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        var serverConnectTask = server.WaitForConnectionAsync();

        using var client = new NamedPipeClient(
            processId,
            pipeName,
            authManager: null,
            certManager: null,
            enforceHostCompatibilityValidation: false,
            requestTimeout: TimeSpan.FromMilliseconds(100));
        (await client.ConnectAsync(TimeSpan.FromSeconds(5), maxRetries: 1)).Should().BeTrue();
        await serverConnectTask.WaitAsync(TimeSpan.FromSeconds(5));

        var semaphoreField = typeof(NamedPipeClient).GetField("_pipeSemaphore", BindingFlags.Instance | BindingFlags.NonPublic);
        semaphoreField.Should().NotBeNull();
        var semaphore = (SemaphoreSlim)semaphoreField!.GetValue(client)!;

        await semaphore.WaitAsync();
        try
        {
            await FluentActions.Invoking(async () => await client.SendRequestAsync(
                    "ping",
                    "queued-timeout-id",
                    new { },
                    CancellationToken.None))
                .Should().ThrowAsync<TimeoutException>();

            client.IsConnected.Should().BeTrue(
                "timing out before the request owns the pipe must not reset another request's transport state");
        }
        finally
        {
            semaphore.Release();
        }
    }

    [Fact]
    public async Task Dispose_WhenRequestIsInFlight_ShouldWaitForResponseBeforeClosingPipe()
    {
        var processId = global::WpfDevTools.Tests.Unit.TestHelpers.NextSyntheticProcessId();
        var pipeName = $"WpfDevTools_Test_{Guid.NewGuid():N}";
        var requestReceived = new TaskCompletionSource<InspectorRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowServerCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        var serverTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();
            var requestJson = await MessageFraming.ReadMessageAsync(server, CancellationToken.None);
            var request = JsonSerializer.Deserialize<InspectorRequest>(requestJson);

            request.Should().NotBeNull();
            requestReceived.SetResult(request!);

            await allowServerCompletion.Task;

            var response = new InspectorResponse
            {
                Id = request!.Id,
                CorrelationId = request.CorrelationId,
                Result = JsonSerializer.SerializeToElement(new { success = true }),
                Error = null
            };

            await MessageFraming.WriteMessageAsync(
                server,
                JsonSerializer.Serialize(response),
                CancellationToken.None);
        });

        using var client = new NamedPipeClient(processId, pipeName);
        (await client.ConnectAsync(TimeSpan.FromSeconds(5), maxRetries: 1)).Should().BeTrue();

        var sendTask = client.SendRequestAsync(
            "ping",
            "dispose-in-flight",
            new { },
            CancellationToken.None);

        await requestReceived.Task;

        var disposeTask = Task.Run(client.Dispose);
        allowServerCompletion.SetResult();

        var response = await sendTask;
        response.Error.Should().BeNull();

        await disposeTask;
        client.IsConnected.Should().BeFalse();
        await serverTask;
    }

    [Fact]
    public async Task ConnectAsync_WhenReconnectStartsDuringInFlightRequest_ShouldNotAbortCurrentRequest()
    {
        var processId = global::WpfDevTools.Tests.Unit.TestHelpers.NextSyntheticProcessId();
        var pipeName = $"WpfDevTools_Test_{Guid.NewGuid():N}";
        var requestReceived = new TaskCompletionSource<InspectorRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowFirstResponse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var firstServer = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            2,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        using var secondServer = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            2,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        var firstServerTask = Task.Run(async () =>
        {
            await firstServer.WaitForConnectionAsync();
            var requestJson = await MessageFraming.ReadMessageAsync(firstServer, CancellationToken.None);
            var request = JsonSerializer.Deserialize<InspectorRequest>(requestJson);

            request.Should().NotBeNull();
            requestReceived.SetResult(request!);

            await allowFirstResponse.Task;

            try
            {
                var response = new InspectorResponse
                {
                    Id = request!.Id,
                    CorrelationId = request.CorrelationId,
                    Result = JsonSerializer.SerializeToElement(new { success = true }),
                    Error = null
                };

                await MessageFraming.WriteMessageAsync(
                    firstServer,
                    JsonSerializer.Serialize(response),
                    CancellationToken.None);
            }
            catch (IOException)
            {
                // Reconnect on the current implementation tears down the active pipe.
            }
        });

        var secondServerTask = Task.Run(async () =>
        {
            await secondServer.WaitForConnectionAsync();
        });

        using var client = new NamedPipeClient(processId, pipeName);
        (await client.ConnectAsync(TimeSpan.FromSeconds(5), maxRetries: 1)).Should().BeTrue();

        var sendTask = client.SendRequestAsync(
            "ping",
            "reconnect-in-flight",
            new { },
            CancellationToken.None);

        await requestReceived.Task;

        var reconnectTask = client.ConnectAsync(TimeSpan.FromSeconds(5), maxRetries: 1);
        await Task.Yield();

        allowFirstResponse.SetResult();

        var response = await sendTask;
        response.Error.Should().BeNull();

        var reconnected = await reconnectTask;
        reconnected.Should().BeTrue();

        await Task.WhenAll(firstServerTask, secondServerTask);
    }
}
