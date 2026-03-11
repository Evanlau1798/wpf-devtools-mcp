using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.Tools;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public class BatchQueryArgumentParserTests
{
    [Fact]
    public void ParseElementTargets_WithSingleElementId_ShouldReturnSingleTarget()
    {
        var arguments = ToJsonElement(new { elementId = "Button_1" });

        var result = BatchQueryArgumentParser.ParseElementTargets(arguments, "elementId", "elementIds");

        result.Error.Should().BeNull();
        result.IsBatch.Should().BeFalse();
        result.Targets.Should().Equal("Button_1");
    }

    [Fact]
    public void ParseElementTargets_WithElementIdsArray_ShouldReturnBatchTargets()
    {
        var arguments = ToJsonElement(new { elementIds = new[] { "Button_1", "Button_2" } });

        var result = BatchQueryArgumentParser.ParseElementTargets(arguments, "elementId", "elementIds");

        result.Error.Should().BeNull();
        result.IsBatch.Should().BeTrue();
        result.Targets.Should().Equal("Button_1", "Button_2");
    }

    [Fact]
    public void ParseElementTargets_WithMixedSingleAndPluralInputs_ShouldReturnStructuredError()
    {
        var arguments = ToJsonElement(new { elementId = "Button_1", elementIds = new[] { "Button_2" } });

        var result = BatchQueryArgumentParser.ParseElementTargets(arguments, "elementId", "elementIds");

        result.Error.Should().NotBeNull();
        var json = JsonSerializer.SerializeToElement(result.Error);
        json.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
        json.GetProperty("error").GetString().Should().Contain("elementId");
        json.GetProperty("error").GetString().Should().Contain("elementIds");
    }

    [Fact]
    public void ParsePropertyTargets_WithPropertyNamesArray_ShouldReturnBatchTargets()
    {
        var arguments = ToJsonElement(new { propertyNames = new[] { "Width", "Height" } });

        var result = BatchQueryArgumentParser.ParseStringTargets(arguments, "propertyName", "propertyNames", requireAtLeastOne: true);

        result.Error.Should().BeNull();
        result.IsBatch.Should().BeTrue();
        result.Targets.Should().Equal("Width", "Height");
    }
}
