using System.Text.Json;
using Xunit;
using FluentAssertions;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Enums;

namespace WpfDevTools.Tests.Unit.Serialization;

public class MessageSerializationTests
{
    [Fact]
    public void InspectorRequest_ShouldSerializeAndDeserialize()
    {
        // Arrange
        var request = new InspectorRequest
        {
            Id = "test-123",
            Method = "get_visual_tree",
            Params = JsonSerializer.SerializeToElement(new { depth = 5 })
        };

        // Act
        var json = JsonSerializer.Serialize(request);
        var deserialized = JsonSerializer.Deserialize<InspectorRequest>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Id.Should().Be("test-123");
        deserialized.Method.Should().Be("get_visual_tree");
        deserialized.Params.Should().NotBeNull();
        deserialized.Params!.Value.GetProperty("depth").GetInt32().Should().Be(5);
    }

    [Fact]
    public void InspectorResponse_ShouldSerializeAndDeserialize()
    {
        // Arrange
        var response = new InspectorResponse
        {
            Id = "test-123",
            Result = JsonSerializer.SerializeToElement(new { success = true }),
            Error = null
        };

        // Act
        var json = JsonSerializer.Serialize(response);
        var deserialized = JsonSerializer.Deserialize<InspectorResponse>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Id.Should().Be("test-123");
        deserialized.Result.Should().NotBeNull();
        deserialized.Error.Should().BeNull();
    }

    [Fact]
    public void InspectorError_ShouldSerializeAndDeserialize()
    {
        // Arrange
        var error = new InspectorError
        {
            Code = ErrorCode.InvalidRequest,
            Message = "Invalid method name",
            Data = JsonSerializer.SerializeToElement(new { method = "unknown" })
        };

        // Act
        var json = JsonSerializer.Serialize(error);
        var deserialized = JsonSerializer.Deserialize<InspectorError>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Code.Should().Be(ErrorCode.InvalidRequest);
        deserialized.Message.Should().Be("Invalid method name");
    }

    [Fact]
    public void EventMessage_ShouldSerializeAndDeserialize()
    {
        // Arrange
        var eventMsg = new EventMessage
        {
            Type = "binding_error",
            Data = JsonSerializer.SerializeToElement(new { element = "Button1", property = "Text" })
        };

        // Act
        var json = JsonSerializer.Serialize(eventMsg);
        var deserialized = JsonSerializer.Deserialize<EventMessage>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Type.Should().Be("binding_error");
        deserialized.Data.GetProperty("element").GetString().Should().Be("Button1");
    }
}
