using FluentAssertions;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerBlueprintShapeRecoveryTests
{
    public static TheoryData<string, string, string, string> ValidJsonShapeFailures => new()
    {
        {
            "[]",
            "$",
            "Array",
            "{ \"schemaVersion\": \"wpfdevtools.ui-blueprint.v1\", \"name\": \"BlueprintName\", \"packs\": [{ \"id\": \"pack-id\", \"version\": \"1.0.0\", \"role\": \"primary\", \"required\": true }], \"primaryPack\": \"pack-id\", \"layout\": { \"kind\": \"pack.block\" } }"
        },
        {
            "{\"schemaVersion\":42}",
            "$.schemaVersion",
            "Number",
            "\"wpfdevtools.ui-blueprint.v1\""
        },
        {
            "{\"schemaVersion\":\"wpfdevtools.ui-blueprint.v1\",\"packs\":null,\"layout\":{\"kind\":\"core.stack\"}}",
            "$.packs",
            "Null",
            "[{ \"id\": \"pack-id\", \"version\": \"1.0.0\", \"role\": \"primary\", \"required\": true }]"
        },
        {
            "{\"schemaVersion\":\"wpfdevtools.ui-blueprint.v1\",\"packs\":[],\"layout\":null}",
            "$.layout",
            "Null",
            "{ \"kind\": \"pack.block\", \"slots\": {} }"
        },
        {
            "{\"schemaVersion\":\"wpfdevtools.ui-blueprint.v1\",\"packs\":[\"bad\"],\"layout\":{\"kind\":\"core.stack\"}}",
            "$.packs[0]",
            "String",
            "{ \"id\": \"pack-id\", \"version\": \"1.0.0\", \"role\": \"primary\", \"required\": true }"
        },
        {
            "{\"schemaVersion\":\"wpfdevtools.ui-blueprint.v1\",\"packs\":[],\"layout\":{\"kind\":\"core.stack\",\"slots\":null}}",
            "$.layout.slots",
            "Null",
            "{ \"slotName\": [{ \"kind\": \"pack.block\" }] }"
        },
        {
            "{\"schemaVersion\":\"wpfdevtools.ui-blueprint.v1\",\"packs\":[],\"layout\":{\"kind\":\"core.stack\",\"slots\":{\"children\":[\"bad\"]}}}",
            "$.layout.slots.children[0]",
            "String",
            "{ \"kind\": \"pack.block\", \"slots\": {} }"
        },
        {
            "{\"schemaVersion\":\"wpfdevtools.ui-blueprint.v1\",\"packs\":[],\"resourceVariants\":{\"theme.dark\":42},\"layout\":{\"kind\":\"core.stack\"}}",
            "$.resourceVariants[\"theme.dark\"]",
            "Number",
            "\"text\""
        },
        {
            "{\"schemaVersion\":\"wpfdevtools.ui-blueprint.v1\",\"packs\":[],\"layout\":{\"kind\":\"core.stack\",\"slots\":{\"action.area\":[null]}}}",
            "$.layout.slots[\"action.area\"][0]",
            "Null",
            "{ \"kind\": \"pack.block\", \"slots\": {} }"
        },
        {
            "{\"schemaVersion\":\"wpfdevtools.ui-blueprint.v1\",\"packs\":[],\"layout\":{\"kind\":\"core.stack\",\"slots\":{\"action]area\":[false]}}}",
            "$.layout.slots[\"action]area\"][0]",
            "False",
            "{ \"kind\": \"pack.block\", \"slots\": {} }"
        }
    };

    [Theory]
    [MemberData(nameof(ValidJsonShapeFailures))]
    public async Task ValidateTool_ShouldReturnCopyReadyRepairForEveryStructuralShapeFailure(
        string blueprintJson,
        string expectedPath,
        string observedValueKind,
        string expectedJsonShape)
    {
        var result = await UiComposerMcpTools.ValidateUiBlueprint(
            blueprintJson,
            cancellationToken: CancellationToken.None);

        result.IsError.Should().BeFalse();
        var error = result.StructuredContent!.Value.GetProperty("errors")[0];
        error.GetProperty("code").GetString().Should().Be("InvalidBlueprintShape");
        error.GetProperty("jsonPath").GetString().Should().Be(expectedPath);
        error.GetProperty("observedValueKind").GetString().Should().Be(observedValueKind);
        error.GetProperty("expectedJsonShape").GetString().Should().Be(expectedJsonShape);
        error.GetProperty("repairSuggestion").GetString().Should().Be($"Replace {expectedPath} with {expectedJsonShape}.");
    }

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
