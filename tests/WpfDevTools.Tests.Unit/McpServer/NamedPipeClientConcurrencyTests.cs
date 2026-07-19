using System.IO.Pipes;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Serialization;
using Xunit;

namespace WpfDevTools.Tests.Unit.McpServer;

[Collection("TimingSensitive")]
public sealed class NamedPipeClientConcurrencyTests
{
    [Fact]
    public async Task SendRequestAsync_QueuedRequests_ShouldReceiveFullExecutionTimeout()
    {
        var processId = TestHelpers.NextSyntheticProcessId();
        var pipeName = $"WpfDevTools_Test_{Guid.NewGuid():N}";
        var firstRequestReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        var serverTask = RunServerAsync(server, firstRequestReceived);
        using var client = new NamedPipeClient(
            processId,
            pipeName,
            authManager: null,
            certManager: null,
            enforceHostCompatibilityValidation: false,
            requestTimeout: TimeSpan.FromSeconds(1));
        (await client.ConnectAsync(TimeSpan.FromSeconds(5), maxRetries: 1)).Should().BeTrue();

        var first = client.SendRequestAsync("first", "first-id", new { }, CancellationToken.None);
        await firstRequestReceived.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var second = client.SendRequestAsync("second", "second-id", new { }, CancellationToken.None);

        var responses = await Task.WhenAll(first, second);

        responses.Select(response => response.Id).Should().Equal("first-id", "second-id");
        client.IsConnected.Should().BeTrue();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task SendRequestAsync_QueueTimeout_ShouldNotResetConnection()
    {
        var processId = TestHelpers.NextSyntheticProcessId();
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
            requestTimeout: TimeSpan.FromMilliseconds(150));
        (await client.ConnectAsync(TimeSpan.FromSeconds(5), maxRetries: 1)).Should().BeTrue();
        await serverConnectTask.WaitAsync(TimeSpan.FromSeconds(2));

        var semaphoreField = typeof(NamedPipeClient).GetField(
            "_pipeSemaphore",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var semaphore = (SemaphoreSlim)semaphoreField.GetValue(client)!;
        await semaphore.WaitAsync();
        try
        {
            using var callerTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await FluentActions.Invoking(async () => await client.SendRequestAsync(
                    "queued",
                    "queued-id",
                    new { },
                    callerTimeout.Token))
                .Should().ThrowAsync<TimeoutException>()
                .WithMessage("*queue*");

            client.IsConnected.Should().BeTrue();
        }
        finally
        {
            semaphore.Release();
        }
    }

    private static async Task RunServerAsync(
        NamedPipeServerStream server,
        TaskCompletionSource firstRequestReceived)
    {
        await server.WaitForConnectionAsync();
        for (var index = 0; index < 2; index++)
        {
            var requestJson = await MessageFraming.ReadMessageAsync(server, CancellationToken.None);
            var request = JsonSerializer.Deserialize<InspectorRequest>(requestJson)!;
            if (index == 0)
            {
                firstRequestReceived.SetResult();
            }

            await Task.Delay(TimeSpan.FromMilliseconds(600));
            var response = new InspectorResponse
            {
                Id = request.Id,
                CorrelationId = request.CorrelationId,
                Result = JsonSerializer.SerializeToElement(new { success = true })
            };
            await MessageFraming.WriteMessageAsync(
                server,
                JsonSerializer.Serialize(response),
                CancellationToken.None);
        }
    }
}
