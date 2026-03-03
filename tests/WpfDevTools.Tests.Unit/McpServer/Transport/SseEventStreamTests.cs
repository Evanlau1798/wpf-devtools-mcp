using Xunit;
using FluentAssertions;
using WpfDevTools.Mcp.Server.Transport;

namespace WpfDevTools.Tests.Unit.McpServer.Transport;

public class SseEventStreamTests
{
    [Fact]
    public async Task SendEvent_ShouldWriteEventToStream()
    {
        // Arrange
        using var memoryStream = new MemoryStream();
        var eventStream = new SseEventStream(memoryStream);
        var eventData = new { type = "test", data = "hello" };

        // Act
        await eventStream.SendEventAsync("test-event", eventData, CancellationToken.None);

        // Assert
        memoryStream.Position = 0;
        using var reader = new StreamReader(memoryStream);
        var content = await reader.ReadToEndAsync();
        content.Should().Contain("event: test-event");
        content.Should().Contain("data:");
        content.Should().Contain("hello");
    }

    [Fact]
    public async Task SendEvent_WithNullEventName_ShouldUseDefaultEvent()
    {
        // Arrange
        using var memoryStream = new MemoryStream();
        var eventStream = new SseEventStream(memoryStream);
        var eventData = new { message = "test" };

        // Act
        await eventStream.SendEventAsync(null, eventData, CancellationToken.None);

        // Assert
        memoryStream.Position = 0;
        using var reader = new StreamReader(memoryStream);
        var content = await reader.ReadToEndAsync();
        content.Should().Contain("data:");
        content.Should().NotContain("event:");
    }

    [Fact]
    public async Task SendEvent_WithCancellation_ShouldThrowOperationCanceledException()
    {
        // Arrange
        using var memoryStream = new MemoryStream();
        var eventStream = new SseEventStream(memoryStream);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await eventStream.SendEventAsync("test", new { }, cts.Token);
        });
    }

    [Fact]
    public void Dispose_ShouldNotThrowException()
    {
        // Arrange
        using var memoryStream = new MemoryStream();
        var eventStream = new SseEventStream(memoryStream);

        // Act & Assert
        eventStream.Dispose();
        // Should not throw
    }
}
