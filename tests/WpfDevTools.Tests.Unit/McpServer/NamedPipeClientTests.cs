using Xunit;
using FluentAssertions;
using WpfDevTools.Mcp.Server;

namespace WpfDevTools.Tests.Unit.McpServer;

public class NamedPipeClientTests
{
    [Fact]
    public async Task ConnectAsync_WithNonExistentPipe_ShouldReturnFalse()
    {
        // Arrange
        var client = new NamedPipeClient(99999); // Non-existent process

        // Act
        var result = await client.ConnectAsync(TimeSpan.FromSeconds(1));

        // Assert
        result.Should().BeFalse();
        client.IsConnected.Should().BeFalse();
    }

    [Fact]
    public void IsConnected_WhenNotConnected_ShouldReturnFalse()
    {
        // Arrange
        var client = new NamedPipeClient(12345);

        // Act & Assert
        client.IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task SendRequestAsync_WhenNotConnected_ShouldThrowException()
    {
        // Arrange
        var client = new NamedPipeClient(12345);
        var request = new { method = "ping" };

        // Act & Assert
        await client.Invoking(c => c.SendRequestAsync("ping", "test-1", request, CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not connected*");
    }

    [Fact]
    public void Dispose_ShouldCleanupResources()
    {
        // Arrange
        var client = new NamedPipeClient(12345);

        // Act
        client.Dispose();

        // Assert
        client.IsConnected.Should().BeFalse();
    }

    [Fact]
    public void GetPipeName_ShouldReturnCorrectFormat()
    {
        // Arrange
        var processId = 12345;
        var client = new NamedPipeClient(processId);

        // Act
        var pipeName = client.PipeName;

        // Assert
        pipeName.Should().Be("WpfDevTools_12345");
    }
}
