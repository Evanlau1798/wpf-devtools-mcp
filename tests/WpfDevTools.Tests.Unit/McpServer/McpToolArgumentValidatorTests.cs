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

    [Fact]
    public void Validate_ElementScreenshotOutputPath_ShouldReturnActionableInvalidArgument()
    {
        var arguments = ToArguments(new
        {
            processId = 12345,
            outputMode = "file",
            outputPath = "C:\\temp\\shot.png"
        });

        var result = McpToolArgumentValidator.Validate("element_screenshot", arguments);

        result.Should().NotBeNull("file mode is resource-backed and must not silently ignore outputPath");
        result!.IsError.Should().BeTrue();
        var payload = result.StructuredContent!.Value;
        payload.GetProperty("success").GetBoolean().Should().BeFalse();
        payload.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
        payload.GetProperty("error").GetString().Should().Contain("outputPath");
        payload.GetProperty("hint").GetString().Should().Contain("resourceUri");
    }

    [Theory]
    [InlineData("click_element", "elementId")]
    [InlineData("scroll_to_element", "elementId")]
    [InlineData("get_template_tree", "elementId")]
    [InlineData("force_binding_update", "propertyName")]
    [InlineData("get_clipping_info", "elementId")]
    [InlineData("get_element_snapshot", "elementId")]
    [InlineData("set_dp_value", "value", "{\"propertyName\":\"Text\"}")]
    [InlineData("modify_viewmodel", "value", "{\"propertyName\":\"Name\"}")]
    [InlineData("drag_and_drop", "targetElementId", "{\"sourceElementId\":\"Source_1\"}")]
    [InlineData("override_style_setter", "elementId", "{\"propertyName\":\"Opacity\",\"value\":1}")]
    [InlineData("wait_for_dp_change_after_mutation", "triggerMutation", "{\"propertyName\":\"Text\"}")]
    public void Validate_RequiredWrapperArgumentMissing_ShouldReturnStructuredError(
        string toolName,
        string missingArgument,
        string presentArgumentsJson = "{}")
    {
        var arguments = ToArguments(presentArgumentsJson);
        arguments["processId"] = JsonSerializer.SerializeToElement(12345);

        var result = McpToolArgumentValidator.Validate(toolName, arguments);

        result.Should().NotBeNull($"{toolName} must fail before SDK reflection invocation");
        result!.IsError.Should().BeTrue();
        var payload = result.StructuredContent!.Value;
        payload.GetProperty("success").GetBoolean().Should().BeFalse();
        payload.GetProperty("errorCode").GetString().Should().Be("MissingRequiredParameter");
        payload.GetProperty("error").GetString().Should().Contain(missingArgument);
        payload.GetProperty("hint").GetString().Should().Contain(missingArgument);
        payload.GetProperty("suggestedAction").GetString().Should().Contain(missingArgument);
    }

    [Theory]
    [InlineData("modify_viewmodel", "{\"propertyName\":\"Name\",\"value\":null}")]
    [InlineData("modify_viewmodel", "{\"propertyName\":\"Name\",\"value\":\"\"}")]
    [InlineData("set_dp_value", "{\"propertyName\":\"Text\",\"value\":null}")]
    [InlineData("set_dp_value", "{\"propertyName\":\"Text\",\"value\":\"\"}")]
    [InlineData("override_style_setter", "{\"elementId\":\"Input_1\",\"propertyName\":\"ToolTip\",\"value\":null}")]
    public void Validate_ValueArgumentPresentWithNullOrEmptyPayload_ShouldAllowCall(
        string toolName,
        string argumentsJson)
    {
        var arguments = ToArguments(argumentsJson);
        arguments["processId"] = JsonSerializer.SerializeToElement(12345);

        var result = McpToolArgumentValidator.Validate(toolName, arguments);

        result.Should().BeNull("a present value argument can intentionally write null or an empty string");
    }

    private static Dictionary<string, JsonElement> ToArguments(object value)
        => JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            JsonSerializer.Serialize(value))!;

    private static Dictionary<string, JsonElement> ToArguments(string json)
        => JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
}
