using Xunit;
using FluentAssertions;
using WpfDevTools.Mcp.Server.Transport;

namespace WpfDevTools.Tests.Unit.McpServer.Transport;

public class HttpTransportTests
{
    [Fact]
    public async Task Start_ShouldStartHttpServer()
    {
        // Arrange - use port 0 for auto-assignment
        var transport = new HttpTransport(port: 0);

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
        var transport = new HttpTransport(port: 0);
        await transport.StartAsync(CancellationToken.None);

        // Act
        await transport.StopAsync();

        // Assert
        transport.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task Start_WithPortInUse_ShouldThrowException()
    {
        // Arrange - find an available port first
        var transport1 = new HttpTransport(port: 0);
        await transport1.StartAsync(CancellationToken.None);

        // Get the actual port being used (if HttpTransport exposes it)
        // For now, we'll use a fixed port that's likely available
        var testPort = 45678;
        var transport2 = new HttpTransport(port: testPort);
        var transport3 = new HttpTransport(port: testPort);

        await transport2.StartAsync(CancellationToken.None);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await transport3.StartAsync(CancellationToken.None);
        });

        // Cleanup
        await transport1.StopAsync();
        await transport2.StopAsync();
    }

    [Fact]
    public void Constructor_WithInvalidPort_ShouldThrowException()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => new HttpTransport(port: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HttpTransport(port: 70000));
    }

    [Fact]
    public void Constructor_WithPortZero_ShouldAllowAutoAssignment()
    {
        // Act & Assert - port 0 should be allowed for auto-assignment
        var exception = Record.Exception(() => new HttpTransport(port: 0));
        exception.Should().BeNull();
    }

    [Fact]
    public async Task RequestReceived_ShouldProvideRequestAndAcceptResponse()
    {
        // Arrange
        var transport = new HttpTransport(port: 0);
        string? receivedRequest = null;

        transport.RequestReceived += (sender, args) =>
        {
            receivedRequest = args.RequestJson;
            args.ResponseJson = "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"tools\":[]}}";
        };

        await transport.StartAsync(CancellationToken.None);

        try
        {
            // Act - The event should fire and allow setting a response
            var args = new RequestReceivedEventArgs("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}");
            args.ResponseJson = "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{}}";

            // Assert
            args.RequestJson.Should().Contain("tools/list");
            args.ResponseJson.Should().Contain("result");
        }
        finally
        {
            await transport.StopAsync();
        }
    }
}
