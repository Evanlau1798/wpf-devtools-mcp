using FluentAssertions;
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
    public void CoreText_ShouldRemainThemeNeutralByDefault()
    {
        var blueprint = Blueprint(
            """[{ "id": "core", "version": "0.1.0", "required": true, "role": "primary" }]""",
            "core",
            """{ "kind": "core.text", "properties": { "text": "Theme neutral" } }""");

        var result = new UiBlueprintRenderer(CreateRegistry()).Render(new RenderBlueprintRequest(blueprint));

        result.Success.Should().BeTrue(result.Errors.FirstOrDefault()?.Message);
        result.Xaml.Should().NotContain("TextFillColorPrimaryBrush")
            .And.NotContain("Foreground=\"\"");
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
