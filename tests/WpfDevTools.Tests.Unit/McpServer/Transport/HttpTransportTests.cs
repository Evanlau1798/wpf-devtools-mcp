using Xunit;
using FluentAssertions;
using WpfDevTools.Mcp.Server.Transport;

namespace WpfDevTools.Tests.Unit.McpServer.Transport;

public class HttpTransportTests
{
    [Fact]
    public async Task Start_ShouldStartHttpServer()
    {
        // Arrange
        var transport = new HttpTransport(port: 3000);

        // Act
        await transport.StartAsync(CancellationToken.None);

        // Assert
        transport.IsRunning.Should().BeTrue();

        // Cleanup
        await transport.StopAsync();
    }

    [Fact]
    public async Task Stop_ShouldStopHttpServer()
    {
        // Arrange
        var transport = new HttpTransport(port: 3000);
        await transport.StartAsync(CancellationToken.None);

        // Act
        await transport.StopAsync();

        // Assert
        transport.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task Start_WithPortInUse_ShouldThrowException()
    {
        // Arrange
        var transport1 = new HttpTransport(port: 3001);
        var transport2 = new HttpTransport(port: 3001);
        await transport1.StartAsync(CancellationToken.None);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await transport2.StartAsync(CancellationToken.None);
        });

        // Cleanup
        await transport1.StopAsync();
    }

    [Fact]
    public void Constructor_WithInvalidPort_ShouldThrowException()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => new HttpTransport(port: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HttpTransport(port: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HttpTransport(port: 70000));
    }
}
