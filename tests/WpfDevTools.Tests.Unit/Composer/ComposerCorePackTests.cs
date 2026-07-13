using FluentAssertions;
using System.Text.Json;
using WpfDevTools.Mcp.Server.Composer.Blueprints;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Mcp.Server.Composer.Rendering;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerCorePackTests
{
    [Fact]
    public void CorePack_ShouldExposeQualifiedLayoutBlocks()
    {
        var registry = CreateRegistry();

        var core = registry.ListPacks().Packs.Single(pack => pack.Id == "core");

        core.Version.Should().Be("0.1.0");
        core.BlockKinds.Should().BeEquivalentTo(
            "core.border",
            "core.columnDefinition",
            "core.grid",
            "core.gridCell",
            "core.rowDefinition",
            "core.stack",
            "core.template",
            "core.text");
    }

    [Fact]
    public void CorePack_ShouldExposeCanonicalArtifactMetadata()
    {
        var root = Path.Combine(TestRepositoryPaths.GetRepoFilePath("."), "packs", "builtin", "core", "0.1.0");
        using var pack = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "pack.json")));
        using var sourceLock = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "source.lock.json")));

        pack.RootElement.GetProperty("kind").GetString().Should().Be("layout-pack");
        pack.RootElement.GetProperty("source").GetProperty("lockFile").GetString().Should().Be("source.lock.json");
        sourceLock.RootElement.GetProperty("generatedAt").GetString().Should().NotBeNullOrWhiteSpace();
        sourceLock.RootElement.GetProperty("generatorSkill").GetString().Should().Be("wpf-extension-pack-creator");
        sourceLock.RootElement.GetProperty("sources")[0].GetProperty("license").GetString().Should().NotBeNullOrWhiteSpace();

        foreach (var blockPath in Directory.GetFiles(Path.Combine(root, "blocks"), "*.block.json"))
        {
            using var block = JsonDocument.Parse(File.ReadAllText(blockPath));
            block.RootElement.GetProperty("description").GetString().Should().NotBeNullOrWhiteSpace(blockPath);
        }
    }

    [Fact]
    public void CorePack_ShouldRenderGridCompositionWithThirdPartyContent()
    {
        var blueprint = Blueprint(
            """
            [
              { "id": "core", "version": "0.1.0", "required": true, "role": "layout-pack" },
              { "id": "wpfui", "version": "0.1.0", "required": true, "role": "primary" }
            ]
            """,
            "wpfui",
            """
            {
              "kind": "core.grid",
              "slots": {
                "rows": [
                  { "kind": "core.rowDefinition", "properties": { "height": "Auto" } },
                  { "kind": "core.rowDefinition", "properties": { "height": "*" } }
                ],
                "columns": [
                  { "kind": "core.columnDefinition", "properties": { "width": "2*" } }
                ],
                "children": [{
                  "kind": "core.gridCell",
                  "properties": { "row": 1, "column": 0, "columnSpan": 1 },
                  "slots": { "content": [{ "kind": "wpfui.button", "properties": { "text": "Run" } }] }
                }]
              }
            }
            """);
        var registry = CreateRegistry();

        var validation = new BlueprintValidationService(registry).Validate(blueprint);
        var render = new UiBlueprintRenderer(registry).Render(new RenderBlueprintRequest(blueprint));

        validation.Success.Should().BeTrue(validation.Errors.FirstOrDefault()?.Message);
        render.Success.Should().BeTrue(render.Errors.FirstOrDefault()?.Message);
        render.Xaml.Should().Contain("<Grid.RowDefinitions>")
            .And.Contain("<RowDefinition Height=\"Auto\"")
            .And.Contain("<ColumnDefinition Width=\"2*\"")
            .And.Contain("Grid.Row=\"1\"")
            .And.Contain("<ui:Button");
    }

    [Fact]
    public void CorePack_ShouldEnforceGenericPropertyConstraints()
    {
        var blueprint = Blueprint(
            """[{ "id": "core", "version": "0.1.0", "required": true, "role": "primary" }]""",
            "core",
            """
            {
              "kind": "core.gridCell",
              "properties": { "row": -1, "columnSpan": 1.5, "margin": "1,2,3" }
            }
            """);

        var result = new BlueprintValidationService(CreateRegistry()).Validate(blueprint);

        result.Errors.Should().Contain(issue => issue.JsonPath == "$.layout.properties.row"
            && issue.Code == "PropertyMinimumViolation");
        result.Errors.Should().Contain(issue => issue.JsonPath == "$.layout.properties.columnSpan"
            && issue.Code == "PropertyIntegerRequired");
        result.Errors.Should().Contain(issue => issue.JsonPath == "$.layout.properties.margin"
            && issue.Code == "PropertyFormatMismatch");
    }

    [Fact]
    public void CoreStack_ShouldApplyDataDrivenSpacingWrapper()
    {
        var blueprint = Blueprint(
            """[{ "id": "core", "version": "0.1.0", "required": true, "role": "primary" }]""",
            "core",
            """
            {
              "kind": "core.stack",
              "properties": { "spacing": "6" },
              "slots": { "children": [{ "kind": "core.text", "properties": { "text": "Ready" } }] }
            }
            """);

        var result = new UiBlueprintRenderer(CreateRegistry()).Render(new RenderBlueprintRequest(blueprint));

        result.Success.Should().BeTrue(result.Errors.FirstOrDefault()?.Message);
        result.Xaml.Should().Contain("<Border Margin=\"6\"><TextBlock");
    }

    [Fact]
    public void CoreText_ShouldInheritThemeForegroundUnlessExplicitlyConfigured()
    {
        var inheritedBlueprint = Blueprint(
            """[{ "id": "core", "version": "0.1.0", "required": true, "role": "primary" }]""",
            "core",
            """{ "kind": "core.text", "properties": { "text": "Theme neutral" } }""");
        var explicitBlueprint = Blueprint(
            """[{ "id": "core", "version": "0.1.0", "required": true, "role": "primary" }]""",
            "core",
            """{ "kind": "core.text", "properties": { "text": "Accent", "foreground": "#123456" } }""");
        var explicitEmptyTextBlueprint = Blueprint(
            """[{ "id": "core", "version": "0.1.0", "required": true, "role": "primary" }]""",
            "core",
            """{ "kind": "core.text", "properties": { "text": "" } }""");

        var renderer = new UiBlueprintRenderer(CreateRegistry());
        var inherited = renderer.Render(new RenderBlueprintRequest(inheritedBlueprint));
        var explicitColor = renderer.Render(new RenderBlueprintRequest(explicitBlueprint));
        var explicitEmptyText = renderer.Render(new RenderBlueprintRequest(explicitEmptyTextBlueprint));

        inherited.Success.Should().BeTrue(inherited.Errors.FirstOrDefault()?.Message);
        inherited.Xaml.Should().NotContain("Foreground=");
        explicitColor.Success.Should().BeTrue(explicitColor.Errors.FirstOrDefault()?.Message);
        explicitColor.Xaml.Should().Contain("Foreground=\"#123456\"");
        explicitEmptyText.Success.Should().BeTrue(explicitEmptyText.Errors.FirstOrDefault()?.Message);
        explicitEmptyText.Xaml.Should().Contain("Text=\"\"");
    }

    [Fact]
    public void WildcardSlot_ShouldNotBypassPackDeclaration()
    {
        var blueprint = Blueprint(
            """[{ "id": "core", "version": "0.1.0", "required": true, "role": "primary" }]""",
            "core",
            """
            {
              "kind": "core.border",
              "slots": { "content": [{ "kind": "wpfui.button" }] }
            }
            """);

        var result = new BlueprintValidationService(CreateRegistry()).Validate(blueprint);

        result.Errors.Should().Contain(issue => issue.JsonPath == "$.layout.slots.content[0]"
            && issue.Code == "PackNotDeclared");
    }

    private static PackRegistry CreateRegistry()
        => PackRegistry.ForRepository(TestRepositoryPaths.GetRepoFilePath("."));

    private static string Blueprint(string packs, string primaryPack, string layout)
        => $$"""
            {
              "schemaVersion": "wpfdevtools.ui-blueprint.v1",
              "name": "CoreComposition",
              "packs": {{packs}},
              "primaryPack": "{{primaryPack}}",
              "layout": {{layout}}
            }
            """;
}
