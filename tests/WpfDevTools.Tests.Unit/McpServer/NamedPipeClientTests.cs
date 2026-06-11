using Xunit;
using FluentAssertions;
using System.IO.Pipes;
using System.Text.Json;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Shared.Configuration;
using WpfDevTools.Shared.Serialization;

namespace WpfDevTools.Tests.Unit.McpServer;

[Collection("TimingSensitive")]
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

    [Fact]
    public async Task ConnectAsync_WithRandomizedProcessPipe_ShouldValidateHostCompatibility()
    {
        var processId = 12345;
        var pipeName = $"WpfDevTools_{processId}_{Guid.NewGuid():N}";
        using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        using var client = new NamedPipeClient(
            processId,
            pipeName,
            authManager: null,
            certManager: null);

        var serverTask = Task.Run(async () =>
        {
            try
            {
                await server.WaitForConnectionAsync();
                using var readCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var requestJson = await MessageFraming.ReadMessageAsync(server, readCts.Token);
                using var request = JsonDocument.Parse(requestJson);
                var requestId = request.RootElement.GetProperty("id").GetString();
                var correlationId = request.RootElement.GetProperty("correlationId").GetString();
                var responseJson = JsonSerializer.Serialize(new
                {
                    id = requestId,
                    result = new
                    {
                        processId = processId + 1,
                        protocolVersion = InspectorCompatibilityContract.ProtocolVersion,
                        buildFingerprint = InspectorCompatibilityContract.GetBuildFingerprint(typeof(NamedPipeClient))
                    },
                    correlationId
                });

                await MessageFraming.WriteMessageAsync(server, responseJson, readCts.Token);
            }
            catch (IOException)
            {
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        });

        var result = await client.ConnectAsync(
            TimeSpan.FromSeconds(5),
            maxRetries: 1,
            CancellationToken.None);

        result.Should().BeFalse();
        client.LastConnectFailure.Should().Be(NamedPipeConnectFailure.ServerProcessMismatch);
        await serverTask.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
