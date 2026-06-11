using Xunit;
using FluentAssertions;
using WpfDevTools.Shared.Messages;
using System.Text.Json;

namespace WpfDevTools.Tests.Unit.Shared;

public class CorrelationIdTests
{
    [Fact]
    public void InspectorRequest_ShouldHaveCorrelationId()
    {
        // Arrange & Act
        var request = new InspectorRequest
        {
            Id = "req-1",
            Method = "get_visual_tree",
            CorrelationId = "corr-123"
        };

        // Assert
        request.CorrelationId.Should().Be("corr-123");
    }

    [Fact]
    public void InspectorResponse_ShouldHaveCorrelationId()
    {
        // Arrange & Act
        var response = new InspectorResponse
        {
            Id = "req-1",
            CorrelationId = "corr-123",
            Result = JsonDocument.Parse("{}").RootElement
        };

        // Assert
        response.CorrelationId.Should().Be("corr-123");
    }

    [Fact]
    public void InspectorRequest_Serialization_ShouldIncludeCorrelationId()
    {
        // Arrange
        var request = new InspectorRequest
        {
            Id = "req-1",
            Method = "get_visual_tree",
            CorrelationId = "corr-123"
        };

        // Act
        var json = JsonSerializer.Serialize(request);
        var deserialized = JsonSerializer.Deserialize<InspectorRequest>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.CorrelationId.Should().Be("corr-123");
    }

    [Fact]
    public void InspectorResponse_Serialization_ShouldIncludeCorrelationId()
    {
        // Arrange
        var response = new InspectorResponse
        {
            Id = "req-1",
            CorrelationId = "corr-123",
            Result = JsonDocument.Parse("{\"success\": true}").RootElement
        };

        // Act
        var json = JsonSerializer.Serialize(response);
        var deserialized = JsonSerializer.Deserialize<InspectorResponse>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.CorrelationId.Should().Be("corr-123");
    }

    [Fact]
    public void CorrelationId_ShouldBeOptional()
    {
        // Arrange & Act - create request without correlation ID
        var request = new InspectorRequest
        {
            Id = "req-1",
            Method = "get_visual_tree"
        };

        // Assert - should not throw
        request.CorrelationId.Should().BeNull();
    }
}
