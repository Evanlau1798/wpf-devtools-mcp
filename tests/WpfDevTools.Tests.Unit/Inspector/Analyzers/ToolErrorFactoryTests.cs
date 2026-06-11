using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Inspector.Utilities;

namespace WpfDevTools.Tests.Unit.Inspector.Analyzers;

public class ToolErrorFactoryTests
{
    [Fact]
    public void ElementNotFound_ShouldReturnStructuredPayload()
    {
        var result = ToolErrorFactory.ElementNotFound("MissingButton");

        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("success").GetBoolean().Should().BeFalse();
        json.GetProperty("errorCode").GetString().Should().Be("ElementNotFound");
        json.GetProperty("error").GetString().Should().Contain("MissingButton");
        json.GetProperty("hint").GetString().Should().Contain("get_visual_tree");
    }

    [Fact]
    public void PropertyNotFound_ShouldIncludePropertyNameAndHint()
    {
        var result = ToolErrorFactory.PropertyNotFound("Width", "Button");

        var json = JsonSerializer.SerializeToElement(result);
        json.GetProperty("success").GetBoolean().Should().BeFalse();
        json.GetProperty("errorCode").GetString().Should().Be("PropertyNotFound");
        json.GetProperty("error").GetString().Should().Contain("Width");
        json.GetProperty("hint").GetString().Should().Contain("propertyName");
    }
}
