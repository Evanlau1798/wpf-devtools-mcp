using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class McpToolArgumentValidatorTests
{
    [Fact]
    public void Validate_FindElementsNameFilter_ShouldReturnActionableInvalidArgument()
    {
        var arguments = ToArguments(new
        {
            processId = 12345,
            nameFilter = "SubmitButton"
        });

        var result = McpToolArgumentValidator.Validate("find_elements", arguments);

        result.Should().NotBeNull();
        result!.IsError.Should().BeTrue();
        var payload = result.StructuredContent!.Value;
        payload.GetProperty("success").GetBoolean().Should().BeFalse();
        payload.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
        payload.GetProperty("error").GetString().Should().Contain("nameFilter");
        payload.GetProperty("hint").GetString().Should().Contain("elementName");
    }

    [Fact]
    public void Validate_FindElementsKnownArguments_ShouldAllowCall()
    {
        var arguments = ToArguments(new
        {
            processId = 12345,
            controlType = "Button",
            elementName = "SubmitButton",
            maxResults = 10
        });

        var result = McpToolArgumentValidator.Validate("find_elements", arguments);

        result.Should().BeNull();
    }

    [Fact]
    public void Validate_FindElementsQueryArgument_ShouldAllowCall()
    {
        var arguments = ToArguments(new
        {
            processId = 12345,
            query = "Apply",
            maxResults = 5
        });

        var result = McpToolArgumentValidator.Validate("find_elements", arguments);

        result.Should().BeNull();
    }

    [Fact]
    public void Validate_SerializeToXamlSelector_ShouldReturnActionableInvalidArgument()
    {
        var arguments = ToArguments(new
        {
            processId = 12345,
            selector = "Button",
            maxDepth = 2,
            maxNodes = 40
        });

        var result = McpToolArgumentValidator.Validate("serialize_to_xaml", arguments);

        result.Should().NotBeNull(
            "stale selector-style calls must not be silently treated as root-window serialization");
        result!.IsError.Should().BeTrue();
        var payload = result.StructuredContent!.Value;
        payload.GetProperty("success").GetBoolean().Should().BeFalse();
        payload.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
        payload.GetProperty("error").GetString().Should().Contain("selector");
        payload.GetProperty("hint").GetString().Should().Contain("find_elements");
        payload.GetProperty("hint").GetString().Should().Contain("elementId");
    }

    [Fact]
    public void Validate_SerializeToXamlKnownArguments_ShouldAllowCall()
    {
        var arguments = ToArguments(new
        {
            processId = 12345,
            elementId = "Button_1"
        });

        var result = McpToolArgumentValidator.Validate("serialize_to_xaml", arguments);

        result.Should().BeNull();
    }

    private static Dictionary<string, JsonElement> ToArguments(object value)
        => JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            JsonSerializer.Serialize(value))!;
}
