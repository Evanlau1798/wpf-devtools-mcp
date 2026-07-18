using FluentAssertions;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerBlueprintCompositionSafetyTests
{
    [Theory]
    [InlineData("{\"content\":{\"kind\":\"core.text\"}}")]
    [InlineData("[]")]
    public async Task ComposeUiBlueprintTool_ShouldRejectMalformedExistingSlotShape(string slotsJson)
    {
        var blueprint = $$"""
            {
              "schemaVersion": "wpfdevtools.ui-blueprint.v1",
              "name": "MalformedSlot",
              "packs": [{ "id": "core", "version": "0.1.0", "required": true, "role": "primary" }],
              "primaryPack": "core",
              "layout": { "kind": "core.border", "slots": {{slotsJson}} }
            }
            """;

        var result = await UiComposerMcpTools.ComposeUiBlueprint(
            blueprint,
            "$.layout.slots.content",
            "core.text",
            cancellationToken: CancellationToken.None);

        result.IsError.Should().BeTrue();
        var issue = result.StructuredContent!.Value.GetProperty("errors")[0];
        issue.GetProperty("code").GetString().Should().Be("CompositionTargetInvalidShape");
    }
}
