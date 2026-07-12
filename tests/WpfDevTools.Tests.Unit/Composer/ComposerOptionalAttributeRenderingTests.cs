using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Contracts;
using WpfDevTools.Mcp.Server.Composer.Rendering;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerOptionalAttributeRenderingTests
{
    [Theory]
    [InlineData("<Border Foreground = '{{foreground}}'>{{item}}</Border>", "<Border>{{item}}</Border>")]
    [InlineData("<Border Tag=\"\" Foreground=\"{{foreground}}\">{{item}}</Border>", "<Border Tag=\"\">{{item}}</Border>")]
    [InlineData("<!-- <Border Foreground=\"{{foreground}}\" /> -->", "<!-- <Border Foreground=\"{{foreground}}\" /> -->")]
    [InlineData("<Border /> Foreground=\"{{foreground}}\"", "<Border /> Foreground=\"{{foreground}}\"")]
    [InlineData("<Border Tag='literal Foreground=\"{{foreground}}\"' />", "<Border Tag='literal Foreground=\"{{foreground}}\"' />")]
    [InlineData("<Border Tag=\"literal Foreground='{{foreground}}'\" />", "<Border Tag=\"literal Foreground='{{foreground}}'\" />")]
    public void UnsetPropertyAttribute_ShouldRespectXamlBoundaries(string template, string expected)
    {
        var result = UiBlueprintRenderer.OmitUnsetPropertyAttributes(template, Node(), Block());

        result.Should().Be(expected);
    }

    [Fact]
    public void ExplicitEmptyPropertyAndUnknownToken_ShouldRemainForNormalTokenResolution()
    {
        const string template = "<Border Foreground=\"{{foreground}}\" Tag=\"{{unknown}}\" />";
        var node = Node();
        node.Properties["foreground"] = JsonSerializer.SerializeToElement(string.Empty);

        var result = UiBlueprintRenderer.OmitUnsetPropertyAttributes(template, node, Block());

        result.Should().Be(template);
    }

    private static UiBlueprintNode Node() => new();

    private static UiBlockDefinition Block()
        => new()
        {
            Properties = new Dictionary<string, UiBlockProperty>(StringComparer.Ordinal)
            {
                ["foreground"] = new() { Type = "string" }
            }
        };
}
