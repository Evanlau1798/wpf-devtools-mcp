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
        using var server = new NamedPipeServerStream("test_pipe_pooling", PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var client = new NamedPipeClientStream(".", "test_pipe_pooling", PipeDirection.InOut, PipeOptions.Asynchronous);

        var connectTask = client.ConnectAsync();
        await server.WaitForConnectionAsync();
        await connectTask;

        var message = new string('X', 1024); // 1KB message

        // Act - write many messages (should reuse buffers)
        var initialMemory = GC.GetTotalMemory(true);

        for (int i = 0; i < 100; i++)
        {
            await MessageFraming.WriteMessageAsync(server, message);
        }

        var finalMemory = GC.GetTotalMemory(false);
        var memoryIncrease = finalMemory - initialMemory;

        // Assert - memory increase should be minimal if pooling works
        // Without pooling: 100 * 1KB = 100KB+ allocations
        // With pooling: should be much less due to buffer reuse
        memoryIncrease.Should().BeLessThan(50 * 1024,
            "buffer pooling should minimize allocations");
    }

    [Fact]
    public async Task ReadMessageAsync_ShouldReuseBuffers()
    {
        // Arrange
        using var server = new NamedPipeServerStream("test_pipe_read_pooling", PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var client = new NamedPipeClientStream(".", "test_pipe_read_pooling", PipeDirection.InOut, PipeOptions.Asynchronous);

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

        var initialMemory = GC.GetTotalMemory(true);

        for (int i = 0; i < 50; i++)
        {
            var received = await MessageFraming.ReadMessageAsync(client);
            received.Should().Be(message);
        }

        await writeTask;

        var finalMemory = GC.GetTotalMemory(false);
        var memoryIncrease = finalMemory - initialMemory;

        // Assert - should not allocate excessively
        memoryIncrease.Should().BeLessThan(100 * 1024,
            "buffer pooling should minimize read allocations");
    }
}
