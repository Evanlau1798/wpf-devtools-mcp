using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Blueprints;
using WpfDevTools.Mcp.Server.Composer.Contracts;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerAdjacentContentAdvisoryTests
{
    [Fact]
    public void AddIssues_ShouldUseExtensionDeclaredRolesAndPropertyNames()
    {
        var warnings = new List<BlueprintValidationIssue>();

        BlueprintAdjacentContentDiagnostics.AddIssues(
            FlowNode("0", "0", "0"),
            "$.layout",
            Blocks(),
            warnings);

        warnings.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            JsonPath = "$.layout.slots.parts[1]",
            Code = "AdjacentContentWithoutSeparation",
            Message = "Separate adjacent copy runs in this horizontal flow.",
            RepairSuggestion = "Increase gutter or either caption outerSpace.",
            ParentSlot = "parts"
        }, options => options.ExcludingMissingMembers());
        warnings[0].RelatedJsonPaths.Should().Contain(
            "$.layout.slots.parts[0]",
            "$.layout.properties.gutter",
            "$.layout.slots.parts[0].properties.outerSpace",
            "$.layout.slots.parts[1].properties.outerSpace");
    }

    [Theory]
    [InlineData("8", "0")]
    [InlineData("0", "0,0,4,0")]
    public void AddIssues_ShouldRespectDeclaredParentOrChildSeparation(
        string gutter,
        string firstOuterSpace)
    {
        var warnings = new List<BlueprintValidationIssue>();

        BlueprintAdjacentContentDiagnostics.AddIssues(
            FlowNode(gutter, firstOuterSpace, "0"),
            "$.layout",
            Blocks(),
            warnings);

        warnings.Should().BeEmpty();
    }

    [Fact]
    public void AddIssues_ShouldBoundAdvisoriesForLargeDeclaredRuns()
    {
        var root = FlowNode("0", "0", "0");
        root.Slots["parts"] = Enumerable.Range(0, 34)
            .Select(_ => Caption("0"))
            .ToArray();
        var warnings = new List<BlueprintValidationIssue>();

        BlueprintAdjacentContentDiagnostics.AddIssues(root, "$.layout", Blocks(), warnings);

        warnings.Should().HaveCount(32);
        warnings[0].JsonPath.Should().Be("$.layout.slots.parts[1]");
        warnings[^1].JsonPath.Should().Be("$.layout.slots.parts[32]");
    }

    [Fact]
    public void AddIssues_ShouldNotSuggestUndeclaredChildMarginPath()
    {
        var blocks = Blocks().ToDictionary();
        blocks["nova.caption"] = new UiBlockDefinition
        {
            Kind = "nova.caption",
            AuthoringRoles = ["copy-run"]
        };
        var root = FlowNode("0", "0", "0");
        root.Slots["parts"][1].Kind = "nova.caption";
        var warnings = new List<BlueprintValidationIssue>();

        BlueprintAdjacentContentDiagnostics.AddIssues(root, "$.layout", blocks, warnings);

        warnings.Should().ContainSingle();
        warnings[0].RelatedJsonPaths.Should()
            .NotContain("$.layout.slots.parts[1].properties.outerSpace");
    }

    private static UiBlueprintNode FlowNode(
        string gutter,
        string firstOuterSpace,
        string secondOuterSpace)
        => new()
        {
            Kind = "nebula.flow",
            Properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["flowMode"] = JsonSerializer.SerializeToElement("Across"),
                ["gutter"] = JsonSerializer.SerializeToElement(gutter)
            },
            Slots = new Dictionary<string, UiBlueprintNode[]>(StringComparer.Ordinal)
            {
                ["parts"] = [Caption(firstOuterSpace), Caption(secondOuterSpace)]
            }
        };

    private static UiBlueprintNode Caption(string outerSpace)
        => new()
        {
            Kind = "orbit.caption",
            Properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["outerSpace"] = JsonSerializer.SerializeToElement(outerSpace)
            }
        };

    private static IReadOnlyDictionary<string, UiBlockDefinition> Blocks()
        => new Dictionary<string, UiBlockDefinition>(StringComparer.Ordinal)
        {
            ["nebula.flow"] = new UiBlockDefinition
            {
                Kind = "nebula.flow",
                Properties = new Dictionary<string, UiBlockProperty>(StringComparer.Ordinal)
                {
                    ["flowMode"] = StringProperty("Across"),
                    ["gutter"] = ThicknessProperty("0")
                },
                Slots = new Dictionary<string, UiBlockSlot>(StringComparer.Ordinal)
                {
                    ["parts"] = new UiBlockSlot
                    {
                        AdjacencyAdvisory = new UiSlotAdjacencyAdvisory
                        {
                            ChildRole = "copy-run",
                            WhenProperty = "flowMode",
                            WhenValues = ["Across"],
                            ItemSpacingProperty = "gutter",
                            ChildMarginProperty = "outerSpace",
                            Message = "Separate adjacent copy runs in this horizontal flow.",
                            RepairSuggestion = "Increase gutter or either caption outerSpace."
                        }
                    }
                }
            },
            ["orbit.caption"] = new UiBlockDefinition
            {
                Kind = "orbit.caption",
                AuthoringRoles = ["copy-run"],
                Properties = new Dictionary<string, UiBlockProperty>(StringComparer.Ordinal)
                {
                    ["outerSpace"] = ThicknessProperty("0")
                }
            }
        };

    private static UiBlockProperty StringProperty(string defaultValue)
        => new()
        {
            Type = "string",
            Default = JsonSerializer.SerializeToElement(defaultValue)
        };

    private static UiBlockProperty ThicknessProperty(string defaultValue)
        => new()
        {
            Type = "string",
            Format = "thickness",
            Default = JsonSerializer.SerializeToElement(defaultValue)
        };
}
