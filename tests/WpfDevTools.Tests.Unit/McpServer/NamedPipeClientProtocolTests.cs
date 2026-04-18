using System.IO.Pipes;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Serialization;

namespace WpfDevTools.Tests.Unit.McpServer;

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
}
