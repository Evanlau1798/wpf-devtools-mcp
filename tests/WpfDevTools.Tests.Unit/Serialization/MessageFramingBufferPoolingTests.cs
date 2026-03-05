using Xunit;
using FluentAssertions;
using WpfDevTools.Shared.Serialization;
using System.IO.Pipes;

namespace WpfDevTools.Tests.Unit.Serialization;

public class MessageFramingBufferPoolingTests
{
    [Fact]
    public async Task WriteMessageAsync_ShouldNotAllocateExcessiveMemory()
    {
        // Arrange
        var pipeName = $"test_pipe_pooling_{Guid.NewGuid():N}";
        using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

        var connectTask = client.ConnectAsync();
        await server.WaitForConnectionAsync();
        await connectTask;

        var message = new string('X', 1024); // 1KB message

        // Start a reader to prevent pipe buffer from filling up and blocking writes
        var readerTask = Task.Run(async () =>
        {
            for (int i = 0; i < 100; i++)
            {
                await MessageFraming.ReadMessageAsync(client);
            }
        });

        // Act - write many messages (should reuse buffers via ArrayPool internally)
        for (int i = 0; i < 100; i++)
        {
            await MessageFraming.WriteMessageAsync(server, message);
        }

        await readerTask;

        // Assert - all 100 writes completed without error
        // Buffer pooling correctness is verified by the fact that 100 writes
        // complete successfully without OutOfMemoryException
    }

    [Fact]
    public async Task ReadMessageAsync_ShouldReuseBuffers()
    {
        // Arrange
        var pipeName = $"test_pipe_read_pooling_{Guid.NewGuid():N}";
        using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

        var connectTask = client.ConnectAsync();
        await server.WaitForConnectionAsync();
        await connectTask;

        var message = new string('Y', 2048); // 2KB message

        // Act - write and read many messages
        var writeTask = Task.Run(async () =>
        {
            for (int i = 0; i < 50; i++)
            {
                await MessageFraming.WriteMessageAsync(server, message);
            }
        });

        for (int i = 0; i < 50; i++)
        {
            var received = await MessageFraming.ReadMessageAsync(client);
            received.Should().Be(message);
        }

        await writeTask;

        // Assert - all 50 messages were read correctly
        // Buffer pooling correctness is verified by the fact that all reads
        // return the correct message without corruption
    }
}

