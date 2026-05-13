using System.IO.Pipes;
using Xunit;
using FluentAssertions;
using WpfDevTools.Shared.Serialization;

namespace WpfDevTools.Tests.Unit.Serialization;

public class MessageFramingTests
{
    [Fact]
    public async Task WriteAndReadMessage_ShouldRoundTrip()
    {
        // Arrange
        var pipeName = "test-pipe-" + Guid.NewGuid();
        var testMessage = "Hello, WPF DevTools!";
        string? result = null;
        using var timeout = CreatePipeTimeout();

        // Act
        var serverTask = Task.Run(async () =>
        {
            using var server = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            await server.WaitForConnectionAsync(timeout.Token);
            await MessageFraming.WriteMessageAsync(server, testMessage, timeout.Token);
        });

        var clientTask = Task.Run(async () =>
        {
            using var client = new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

            await client.ConnectAsync(5000, timeout.Token);
            result = await MessageFraming.ReadMessageAsync(client, timeout.Token);
        });

        await AwaitPipeTasksAsync(serverTask, clientTask);

        // Assert
        result.Should().Be(testMessage);
    }

    [Fact]
    public async Task WriteAndReadLargeMessage_ShouldRoundTrip()
    {
        // Arrange
        var pipeName = "test-pipe-large-" + Guid.NewGuid();
        var testMessage = new string('A', 100_000); // 100 KB message
        string? result = null;
        using var timeout = CreatePipeTimeout();

        // Act
        var serverTask = Task.Run(async () =>
        {
            using var server = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            await server.WaitForConnectionAsync(timeout.Token);
            await MessageFraming.WriteMessageAsync(server, testMessage, timeout.Token);
        });

        var clientTask = Task.Run(async () =>
        {
            using var client = new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

            await client.ConnectAsync(5000, timeout.Token);
            result = await MessageFraming.ReadMessageAsync(client, timeout.Token);
        });

        await AwaitPipeTasksAsync(serverTask, clientTask);

        // Assert
        result.Should().Be(testMessage);
        result!.Length.Should().Be(100_000);
    }

    [Fact]
    public async Task ReadMessage_WithInvalidLength_ShouldThrow()
    {
        // Arrange
        var pipeName = "test-pipe-invalid-" + Guid.NewGuid();
        Exception? caughtException = null;
        using var timeout = CreatePipeTimeout();

        // Act
        var serverTask = Task.Run(async () =>
        {
            using var server = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            await server.WaitForConnectionAsync(timeout.Token);

            // Write invalid length (negative)
            var invalidLength = BitConverter.GetBytes(-1);
            await server.WriteAsync(invalidLength, timeout.Token);
            await server.FlushAsync(timeout.Token);
        });

        var clientTask = Task.Run(async () =>
        {
            using var client = new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

            await client.ConnectAsync(5000, timeout.Token);

            try
            {
                await MessageFraming.ReadMessageAsync(client, timeout.Token);
            }
            catch (Exception ex)
            {
                caughtException = ex;
            }
        });

        await AwaitPipeTasksAsync(serverTask, clientTask);

        // Assert
        caughtException.Should().NotBeNull();
        caughtException.Should().BeOfType<InvalidOperationException>();
    }

    private static CancellationTokenSource CreatePipeTimeout()
        => new(TimeSpan.FromSeconds(10));

    private static async Task AwaitPipeTasksAsync(Task serverTask, Task clientTask)
        => await Task.WhenAll(serverTask, clientTask).WaitAsync(TimeSpan.FromSeconds(10));
}
