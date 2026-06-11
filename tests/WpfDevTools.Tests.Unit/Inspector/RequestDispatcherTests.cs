using Xunit;
using FluentAssertions;
using System.Text.Json;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Enums;

namespace WpfDevTools.Tests.Unit.Inspector;

public class RequestDispatcherTests
{
    [Fact]
    public async Task DispatchRequest_WithUnknownMethod_ShouldReturnMethodNotFoundError()
    {
        // Arrange
        var dispatcher = new WpfDevTools.Inspector.Host.RequestDispatcher(new WpfDevTools.Shared.Utilities.FileLogger());
        var request = new InspectorRequest
        {
            Id = "test-1",
            Method = "unknown_method",
            Params = null,
            CorrelationId = "corr-1"
        };

        // Act
        var response = await dispatcher.DispatchAsync(request, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.Id.Should().Be("test-1");
        response.CorrelationId.Should().Be("corr-1");
        response.Result.Should().BeNull();
        response.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be(ErrorCode.MethodNotFound);
        response.Error.Message.Should().Contain("unknown_method");
    }

    [Fact]
    public async Task DispatchRequest_WithPingMethod_ShouldReturnPongResult()
    {
        // Arrange
        var dispatcher = new WpfDevTools.Inspector.Host.RequestDispatcher(new WpfDevTools.Shared.Utilities.FileLogger());
        var request = new InspectorRequest
        {
            Id = "test-2",
            Method = "ping",
            Params = null
        };

        // Act
        var response = await dispatcher.DispatchAsync(request, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.Id.Should().Be("test-2");
        response.Error.Should().BeNull();
        response.Result.Should().NotBeNull();

        var result = response.Result!.Value;
        result.TryGetProperty("success", out var successProp).Should().BeTrue();
        successProp.GetBoolean().Should().BeTrue();
        result.TryGetProperty("status", out var statusProp).Should().BeTrue();
        statusProp.GetString().Should().Be("pong");
    }

    [Fact]
    public async Task DispatchRequest_WithPingMethod_ShouldPreserveCorrelationId()
    {
        // Arrange
        var dispatcher = new WpfDevTools.Inspector.Host.RequestDispatcher(new WpfDevTools.Shared.Utilities.FileLogger());
        var request = new InspectorRequest
        {
            Id = "test-2b",
            Method = "ping",
            Params = null,
            CorrelationId = "corr-ping"
        };

        // Act
        var response = await dispatcher.DispatchAsync(request, CancellationToken.None);

        // Assert
        response.Error.Should().BeNull();
        response.CorrelationId.Should().Be("corr-ping");
    }

    [Fact]
    public async Task DispatchRequest_WithRemovedTestSlowMethod_ShouldReturnMethodNotFound()
    {
        // Arrange - test_slow debug handler was removed; dispatching it should return MethodNotFound
        var dispatcher = new WpfDevTools.Inspector.Host.RequestDispatcher(new WpfDevTools.Shared.Utilities.FileLogger());
        var request = new InspectorRequest
        {
            Id = "test-3",
            Method = "test_slow",
            Params = null
        };

        // Act
        var response = await dispatcher.DispatchAsync(request, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.Id.Should().Be("test-3");
        response.Result.Should().BeNull();
        response.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be(ErrorCode.MethodNotFound);
        response.Error.Message.Should().Contain("test_slow");
    }

    [Fact]
    public async Task DispatchRequest_WithInvalidParams_ShouldReturnGracefulResult()
    {
        // Arrange
        var dispatcher = new WpfDevTools.Inspector.Host.RequestDispatcher(new WpfDevTools.Shared.Utilities.FileLogger());
        var request = new InspectorRequest
        {
            Id = "test-4",
            Method = "get_visual_tree",
            Params = JsonSerializer.SerializeToElement(new { invalid = "params" })
        };

        // Act
        var response = await dispatcher.DispatchAsync(request, CancellationToken.None);

        // Assert - Without WPF Application context, analyzers gracefully degrade
        // via DispatcherAnalyzerBase and return a result (not an error)
        response.Should().NotBeNull();
        response.Id.Should().Be("test-4");
        response.Result.Should().NotBeNull();
    }
}
