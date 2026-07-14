using FluentAssertions;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerBlueprintShapeRecoveryTests
{
    [Fact]
    public async Task ValidateTool_ShouldMapValidJsonShapeMismatchToExactRepair()
    {
        var result = await UiComposerMcpTools.ValidateUiBlueprint(
            """
            {
              "schemaVersion": "wpfdevtools.ui-blueprint.v1",
              "name": "ShapeProbe",
              "packs": [{ "id": "core", "version": "0.1.0", "role": "primary", "required": true }],
              "primaryPack": "core",
              "layout": {
                "kind": "core.stack",
                "slots": { "children": [{ "kind": "core.stack", "slots": "not-an-object" }] }
              }
            }
            """,
            cancellationToken: CancellationToken.None);

        result.IsError.Should().BeFalse();
        var error = result.StructuredContent!.Value.GetProperty("errors")[0];
        error.GetProperty("code").GetString().Should().Be("InvalidBlueprintShape");
        error.GetProperty("jsonPath").GetString()
            .Should().Be("$.layout.slots.children[0].slots");
        error.GetProperty("observedValueKind").GetString().Should().Be("String");
        error.GetProperty("expectedJsonShape").GetString()
            .Should().Be("{ \"slotName\": [{ \"kind\": \"pack.block\" }] }");
        error.GetProperty("repairSuggestion").GetString()
            .Should().Contain("Replace $.layout.slots.children[0].slots");
    }

    [Fact]
    public async Task ValidateTool_ShouldKeepMalformedJsonClassifiedAsSyntaxFailure()
    {
        var result = await UiComposerMcpTools.ValidateUiBlueprint(
            """{ "schemaVersion": "wpfdevtools.ui-blueprint.v1", "layout": [ }""",
            cancellationToken: CancellationToken.None);

        var error = result.StructuredContent!.Value.GetProperty("errors")[0];
        error.GetProperty("code").GetString().Should().Be("InvalidBlueprintJson");
        error.GetProperty("jsonPath").GetString().Should().Be("$");
        error.TryGetProperty("observedValueKind", out _).Should().BeFalse();
    }
}
