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
            elementName = "SubmitButton",
            maxResults = 10
        });

        var result = McpToolArgumentValidator.Validate("find_elements", arguments);

        result.Should().BeNull();
    }

    private static Dictionary<string, JsonElement> ToArguments(object value)
        => JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            JsonSerializer.Serialize(value))!;
}
