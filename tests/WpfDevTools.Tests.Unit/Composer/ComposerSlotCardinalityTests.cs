using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Blueprints;
using WpfDevTools.Mcp.Server.Composer.Contracts;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerSlotCardinalityTests
{
    [Theory]
    [InlineData(2, 4, 1, "SlotMinimumItemsNotMet")]
    [InlineData(0, 1, 2, "SlotMaximumItemsExceeded")]
    public void CardinalityValidator_ShouldEnforceExtensionDeclaredBounds(
        int minItems,
        int maxItems,
        int childCount,
        string expectedCode)
    {
        var node = new UiBlueprintNode
        {
            Kind = "thirdparty.panel",
            Slots = new Dictionary<string, UiBlueprintNode[]>(StringComparer.Ordinal)
            {
                ["items"] = Enumerable.Range(0, childCount)
                    .Select(_ => new UiBlueprintNode { Kind = "thirdparty.item" })
                    .ToArray()
            }
        };
        var block = new UiBlockDefinition
        {
            Kind = "thirdparty.panel",
            Slots = new Dictionary<string, UiBlockSlot>(StringComparer.Ordinal)
            {
                ["items"] = new()
                {
                    AllowedKinds = ["thirdparty.item"],
                    MinItems = minItems,
                    MaxItems = maxItems
                }
            }
        };
        var errors = new List<BlueprintValidationIssue>();

        BlueprintSlotCardinalityValidator.AddIssues(node, "$.layout", block, errors);

        errors.Should().ContainSingle(issue =>
            issue.Code == expectedCode
            && issue.JsonPath == "$.layout.slots.items"
            && issue.ParentSlot == "items");
    }
}
