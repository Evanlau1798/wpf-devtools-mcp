using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Blueprints;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Mcp.Server.McpTools;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerBlueprintRepairTests
{
    [Fact]
    public void RepairBlueprint_ShouldMapValidationErrorsToBlueprintActions()
    {
        var projectRoot = CreateTempProjectWithRepairPack();
        try
        {
            var repair = new BlueprintRepairService(CreateRegistry(projectRoot));
            var result = repair.Repair(new BlueprintRepairRequest(RepairPackBlueprint("""
                {
                  "kind": "repair.panel",
                  "properties": { "extra": true },
                  "slots": {
                    "content": [{ "kind": "repair.panel" }]
                  }
                }
                """)));

            result.Success.Should().BeTrue();
            result.Repairable.Should().BeTrue();
            result.Actions.Should().Contain(action => action.IssueCode == "RequiredPropertyMissing"
                && action.RepairKind == "add-property"
                && action.Target == "blueprint"
                && action.SuggestedValue!.Value.GetString() == "Untitled");
            result.Actions.Should().Contain(action => action.IssueCode == "SlotChildKindNotAllowed"
                && action.RepairKind == "replace-child-kind"
                && action.AllowedKinds.Contains("repair.item"));
            result.Actions.Should().Contain(action => action.IssueCode == "UnknownProperty"
                && action.Source == "validation-warning"
                && action.RepairKind == "review-blueprint");
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RepairBlueprint_ShouldSuggestCatalogBlockAndPackImport()
    {
        var repair = new BlueprintRepairService(CreateRegistry());
        var result = repair.Repair(new BlueprintRepairRequest(Blueprint("""
            {
              "packs": [{ "id": "missing", "version": "0.1.0", "required": true }],
              "primaryPack": "missing",
              "layout": { "kind": "wpfui.missing" }
            }
            """)));

        result.Actions.Should().Contain(action => action.IssueCode == "PackNotFound"
            && action.RepairKind == "import-pack");
        result.Actions.Should().Contain(action => action.IssueCode == "PackNotDeclared"
            && action.RepairKind == "import-pack");
    }

    [Fact]
    public void RepairBlueprint_ShouldMapRendererTokenMismatchToTemplateAction()
    {
        var projectRoot = CreateTempProjectWithRepairPack();
        try
        {
            var repair = new BlueprintRepairService(CreateRegistry(projectRoot));
            var result = repair.Repair(new BlueprintRepairRequest(RepairPackBlueprint("""
                { "kind": "repair.badToken" }
                """)));

            result.Actions.Should().ContainSingle(action => action.IssueCode == "RendererTokenMismatch"
                && action.Target == "renderer-template"
                && action.RepairKind == "fix-renderer-template"
                && action.RendererTemplatePath!.EndsWith("badToken.xaml.sbn", StringComparison.Ordinal));
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void RepairBlueprint_ShouldMapPreviewCompileDiagnosticWithoutPatchingXaml()
    {
        var repair = new BlueprintRepairService(CreateRegistry());
        var diagnosticsJson = """
            {
              "jsonrpc": "2.0",
              "id": 1,
              "result": {
                "structuredContent": {
                  "success": true,
                  "diagnostics": [{
                    "code": "XamlCompileFailed",
                    "message": "Generated preview XAML did not compile.",
                    "jsonPath": "$.layout.slots.content[0]",
                    "rendererTemplatePath": "packs/builtin/wpfui/0.1.0/renderers/xaml/button.xaml.sbn"
                  }]
                }
              }
            }
            """;

        var result = repair.Repair(new BlueprintRepairRequest(
            Blueprint("""{ "layout": { "kind": "wpfui.button" } }"""),
            diagnosticsJson));

        result.GeneratedXamlPatch.Should().BeFalse();
        result.Actions.Should().ContainSingle(action => action.IssueCode == "XamlCompileFailed"
            && action.Target == "blueprint"
            && action.JsonPath == "$.layout.slots.content[0]"
            && action.RendererTemplatePath!.EndsWith("button.xaml.sbn", StringComparison.Ordinal)
            && action.SuggestedAction.Contains("before editing generated XAML", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RepairUiBlueprintTool_ShouldReturnStructuredRepairPlan()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var result = await UiComposerMcpTools.RepairUiBlueprint(
                Blueprint("""{ "layout": { "kind": "button" } }"""),
                localAppDataRoot: tempRoot,
                cancellationToken: CancellationToken.None);

            result.IsError.Should().BeFalse();
            var payload = result.StructuredContent!.Value;
            payload.GetProperty("success").GetBoolean().Should().BeTrue();
            payload.GetProperty("generatedXamlPatch").GetBoolean().Should().BeFalse();
            payload.GetProperty("actionCount").GetInt32().Should().Be(1);
            payload.GetProperty("actions")[0].GetProperty("target").GetString().Should().Be("blueprint");
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    private static PackRegistry CreateRegistry(string? projectRoot = null)
    {
        var repoRoot = TestRepositoryPaths.GetRepoFilePath(".");
        return new PackRegistry(
            ComposerPackPaths.BuiltinRoot(repoRoot),
            projectRoot is null ? null : ComposerPackPaths.ProjectLocalRoot(projectRoot),
            null);
    }

    private static string Blueprint(string body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var packs = root.TryGetProperty("packs", out var packElement)
            ? packElement.GetRawText()
            : """[{ "id": "wpfui", "version": "0.1.0", "required": true }]""";
        var primaryPack = root.TryGetProperty("primaryPack", out var primaryElement)
            ? primaryElement.GetRawText()
            : JsonSerializer.Serialize("wpfui");
        var layout = root.GetProperty("layout").GetRawText();

        return $$"""
            {
              "schemaVersion": "wpfdevtools.ui-blueprint.v1",
              "name": "RepairDemo",
              "packs": {{packs}},
              "primaryPack": {{primaryPack}},
              "layout": {{layout}}
            }
            """;
    }

    private static string RepairPackBlueprint(string layoutJson)
        => $$"""
            {
              "schemaVersion": "wpfdevtools.ui-blueprint.v1",
              "name": "RepairDemo",
              "packs": [{ "id": "repair", "version": "1.0.0", "required": true }],
              "primaryPack": "repair",
              "layout": {{layoutJson}}
            }
            """;

    private static string CreateTempProjectWithRepairPack()
    {
        var projectRoot = CreateTempDirectory();
        var packRoot = Path.Combine(projectRoot, ".wpfdevtools", "packs", "repair", "1.0.0");
        Directory.CreateDirectory(Path.Combine(packRoot, "blocks"));
        Directory.CreateDirectory(Path.Combine(packRoot, "recipes"));
        Directory.CreateDirectory(Path.Combine(packRoot, "examples"));
        Directory.CreateDirectory(Path.Combine(packRoot, "renderers", "xaml"));

        File.WriteAllText(Path.Combine(packRoot, "install.manifest.json"), """
            {"schemaVersion":"wpfdevtools.pack-install-manifest.v1","id":"repair","version":"1.0.0","scope":"project-local","path":".","enabled":true}
            """);
        File.WriteAllText(Path.Combine(packRoot, "pack.json"), """
            {"schemaVersion":"wpfdevtools.ui-pack.v1","id":"repair","displayName":"Repair Pack","version":"1.0.0","blocks":["repair.panel","repair.item","repair.badToken"],"recipes":[]}
            """);
        File.WriteAllText(Path.Combine(packRoot, "source.lock.json"), """
            {"schemaVersion":"wpfdevtools.source-lock.v1","sources":[],"transformPolicy":{}}
            """);
        File.WriteAllText(Path.Combine(packRoot, "blocks", "panel.block.json"), """
            {"schemaVersion":"wpfdevtools.ui-block.v1","kind":"repair.panel","displayName":"Panel","category":"test","properties":{"title":{"type":"string","required":true,"default":"Untitled"}},"slots":{"content":{"allowedKinds":["repair.item"]}},"renderer":{"xamlTemplate":"renderers/xaml/panel.xaml.sbn"},"sourceHints":[]}
            """);
        File.WriteAllText(Path.Combine(packRoot, "blocks", "item.block.json"), """
            {"schemaVersion":"wpfdevtools.ui-block.v1","kind":"repair.item","displayName":"Item","category":"test","properties":{},"slots":{},"renderer":{"xamlTemplate":"renderers/xaml/item.xaml.sbn"},"sourceHints":[]}
            """);
        File.WriteAllText(Path.Combine(packRoot, "blocks", "badToken.block.json"), """
            {"schemaVersion":"wpfdevtools.ui-block.v1","kind":"repair.badToken","displayName":"Bad Token","category":"test","properties":{},"slots":{},"renderer":{"xamlTemplate":"renderers/xaml/badToken.xaml.sbn"},"sourceHints":[]}
            """);
        File.WriteAllText(Path.Combine(packRoot, "renderers", "xaml", "panel.xaml.sbn"), "<TextBlock Text=\"{{ title }}\" />");
        File.WriteAllText(Path.Combine(packRoot, "renderers", "xaml", "item.xaml.sbn"), "<TextBlock />");
        File.WriteAllText(Path.Combine(packRoot, "renderers", "xaml", "badToken.xaml.sbn"), "<TextBlock Text=\"{{ missingToken }}\" />");
        return projectRoot;
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "wpfdevtools-composer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
