using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Blueprints;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Mcp.Server.McpTools;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerBlueprintCompositionIdentityTests
{
    [Fact]
    public async Task ComposeTool_ShouldAssignStandardIdentityAndSummarizeIt()
    {
        var result = await UiComposerMcpTools.ComposeUiBlueprint(
            Blueprint(),
            targetPath: "$.layout.slots.children",
            kind: "wpfui.button",
            elementName: "ConfirmAction",
            automationId: "checkout-confirm",
            cancellationToken: CancellationToken.None);

        result.IsError.Should().BeFalse();
        var payload = result.StructuredContent!.Value;
        var inserted = payload.GetProperty("blueprint").GetProperty("layout")
            .GetProperty("slots").GetProperty("children")[0];
        inserted.GetProperty("elementName").GetString().Should().Be("ConfirmAction");
        inserted.GetProperty("automationId").GetString().Should().Be("checkout-confirm");
        var summary = payload.GetProperty("insertedNodeSummary");
        summary.GetProperty("elementName").GetString().Should().Be("ConfirmAction");
        summary.GetProperty("automationId").GetString().Should().Be("checkout-confirm");
    }

    [Fact]
    public void Compose_ShouldValidateAssignedIdentityThroughBlueprintRules()
    {
        var result = CreateService().Compose(
            Blueprint(rootElementName: "ConfirmAction"),
            "$.layout.slots.children",
            "wpfui.button",
            elementName: "ConfirmAction");

        result.Composed.Should().BeFalse();
        result.Validation!.Errors.Should().ContainSingle(issue =>
            issue.Code == "DuplicateElementName"
            && issue.JsonPath == "$.layout.slots.children[0].elementName");
    }

    private static BlueprintCompositionService CreateService()
    {
        var repoRoot = TestRepositoryPaths.GetRepoFilePath(".");
        return new BlueprintCompositionService(new PackRegistry(ComposerPackPaths.BuiltinRoot(repoRoot)));
    }

    private static string Blueprint(string? rootElementName = null)
        => $$"""
           {
             "schemaVersion": "wpfdevtools.ui-blueprint.v1",
             "name": "IdentityComposition",
             "packs": [
               { "id": "core", "version": "0.1.0", "required": true, "role": "layout-pack" },
               { "id": "wpfui", "version": "0.1.0", "required": true, "role": "primary" }
             ],
             "primaryPack": "wpfui",
             "layout": {
               "kind": "core.stack",
               {{(rootElementName is null ? "" : $"\"elementName\": \"{rootElementName}\"," )}}
               "slots": { "children": [] }
             }
           }
           """;
}
