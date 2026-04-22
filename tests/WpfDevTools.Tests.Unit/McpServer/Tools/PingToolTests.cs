using Xunit;
using FluentAssertions;
using System.Text.Json;
using WpfDevTools.Inspector.Host;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

[Collection("TimingSensitive")]
public class PingToolTests
{
    [Fact]
    public async Task Execute_WithoutConnection_ShouldReturnError()
    {
        // Arrange
        var tool = new PingTool(new SessionManager());
        var parameters = new { processId = 12345 };

        // Act
        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().Contain("not connected");
    }

    [Fact]
    public async Task Execute_WithMissingProcessId_ShouldReturnError()
    {
        // Arrange
        var tool = new PingTool(new SessionManager());
        var parameters = new { };

        // Act
        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().Contain("processId");
    }

    [Fact]
    public async Task Execute_WithValidSession_ShouldReturnSuccess()
    {
        // Arrange
        var processId = global::WpfDevTools.Tests.Unit.TestHelpers.NextSyntheticProcessId();
        using var host = new InspectorHost(processId);
        host.Start();

        var sessionManager = new SessionManager();
        sessionManager.AddSession(processId);
        var client = sessionManager.GetPipeClient(processId);
        client.Should().NotBeNull();
        (await client!.ConnectAsync(TimeSpan.FromSeconds(5), maxRetries: 1)).Should().BeTrue();

        var tool = new PingTool(sessionManager);
        var parameters = new { processId };

        // Act
        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var resultJson = JsonSerializer.SerializeToElement(result);
        resultJson.GetProperty("success").GetBoolean().Should().BeTrue();
        resultJson.GetProperty("status").GetString().Should().Be("connected");
        resultJson.GetProperty("processId").GetInt32().Should().Be(processId);
        resultJson.GetProperty("latencyMs").GetInt64().Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public async Task Execute_WithSessionButNoPipeConnection_ShouldReturnStructuredNotConnectedError()
    {
        // Arrange
        var sessionManager = new SessionManager();
        sessionManager.AddSession(23456);
        var tool = new PingTool(sessionManager);

        // Act
        var result = await tool.ExecuteAsync(ToJsonElement(new { processId = 23456 }), CancellationToken.None);

        // Assert
        var resultJson = JsonSerializer.SerializeToElement(result);
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("errorCode").GetString().Should().Be("NotConnected");
        resultJson.GetProperty("hint").GetString().Should().Contain("connect");
    }

    [Fact]
    public async Task Execute_WithSuccessfulPing_ShouldNotPiggybackDrainEvents()
    {
        const int processId = 24567;
        using var connected = await ConnectedWaitSessionBuilder.CreateAsync(
            processId,
            new object(),
            static (request, _) => Task.FromResult<object>(request.Method switch
            {
                "ping" => new { success = true, pong = true },
                "drain_events" => new
                {
                    success = true,
                    pendingEventCount = 0,
                    droppedEventCount = 0,
                    pendingEvents = Array.Empty<object>()
                },
                _ => new { success = true }
            }));
        var tool = new PingTool(connected.SessionManager);

        var result = await tool.ExecuteAsync(ToJsonElement(new { processId }), CancellationToken.None);

        var resultJson = JsonSerializer.SerializeToElement(result);

        resultJson.GetProperty("success").GetBoolean().Should().BeTrue();
        connected.RequestMethods.Should().Equal("ping");
    }
}
