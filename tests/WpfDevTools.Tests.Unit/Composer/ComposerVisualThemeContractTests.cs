using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Blueprints;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Mcp.Server.Composer.Rendering;
using WpfDevTools.Mcp.Server.McpTools;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerVisualThemeContractTests
{
    [Fact]
    public void RenderBlueprint_ShouldUseSelectedThirdPartyResourceVariant()
    {
        var projectRoot = CreateProjectWithThemePack();
        try
        {
            var result = new UiBlueprintRenderer(CreateRegistry(projectRoot)).Render(
                new RenderBlueprintRequest(Blueprint("light"), ProjectRoot: projectRoot));

            result.Success.Should().BeTrue();
            result.RequiredResources.Should().Equal("<nebula:Theme Mode=\"Light\" />");
        }
        finally
        {
            TestDirectory.Delete(projectRoot);
        }
    }

    [Fact]
    public void ValidateBlueprint_ShouldWarnForLightSurfaceAroundDarkThemeControls()
    {
        var projectRoot = CreateProjectWithThemePack();
        try
        {
            var result = new BlueprintValidationService(CreateRegistry(projectRoot)).Validate(Blueprint("dark"));

            result.Success.Should().BeTrue();
            result.Warnings.Should().ContainSingle(issue =>
                issue.Code == "SurfaceThemeContrastRisk"
                && issue.JsonPath == "$.layout.properties.background"
                && issue.Message.Contains("nebula", StringComparison.Ordinal)
                && issue.RepairSuggestion.Contains("resource variant", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TestDirectory.Delete(projectRoot);
        }
    }

    [Fact]
    public void ValidateBlueprint_ShouldAcceptSurfaceMatchingSelectedTheme()
    {
        var projectRoot = CreateProjectWithThemePack();
        try
        {
            var result = new BlueprintValidationService(CreateRegistry(projectRoot)).Validate(Blueprint("light"));

            result.Success.Should().BeTrue();
            result.Warnings.Should().NotContain(issue => issue.Code == "SurfaceThemeContrastRisk");
        }
        finally
        {
            TestDirectory.Delete(projectRoot);
        }
    }

    [Fact]
    public void ValidateBlueprint_ShouldRejectUnknownResourceVariant()
    {
        var projectRoot = CreateProjectWithThemePack();
        try
        {
            var result = new BlueprintValidationService(CreateRegistry(projectRoot)).Validate(Blueprint("sepia"));

            result.Success.Should().BeFalse();
            result.Errors.Should().ContainSingle(issue =>
                issue.Code == "UnknownResourceVariant"
                && issue.JsonPath == "$.resourceVariants.nebula"
                && issue.AllowedValues.SequenceEqual(new[] { "dark", "light" }));
        }
        finally
        {
            TestDirectory.Delete(projectRoot);
        }
    }

    [Fact]
    public void ValidateBlueprint_ShouldTolerateDuplicateIdenticalPackReferences()
    {
        var projectRoot = CreateProjectWithThemePack();
        try
        {
            var blueprint = Blueprint("light").Replace(
                "\"packs\": [{ \"id\": \"nebula\", \"version\": \"1.0.0\", \"required\": true, \"role\": \"primary\" }]",
                "\"packs\": ["
                + "{ \"id\": \"nebula\", \"version\": \"1.0.0\", \"required\": true, \"role\": \"primary\" },"
                + "{ \"id\": \"nebula\", \"version\": \"1.0.0\", \"required\": true, \"role\": \"primary\" }]",
                StringComparison.Ordinal);

            var result = new BlueprintValidationService(CreateRegistry(projectRoot)).Validate(blueprint);

            result.Success.Should().BeTrue();
        }
        finally
        {
            TestDirectory.Delete(projectRoot);
        }
    }

    [Fact]
    public async Task ListUiBlockPacks_ShouldExposePackOwnedResourceVariants()
    {
        var projectRoot = CreateProjectWithThemePack();
        try
        {
            var result = await UiComposerMcpTools.ListUiBlockPacks(
                projectRoot,
                localAppDataRoot: projectRoot,
                cancellationToken: CancellationToken.None);

            var payload = result.StructuredContent!.Value;
            var pack = payload.GetProperty("packs").EnumerateArray()
                .Single(item => item.GetProperty("id").GetString() == "nebula");
            var resourceVariants = pack.GetProperty("resourceVariants");
            resourceVariants.GetProperty("defaultVariant").GetString().Should().Be("dark");
            resourceVariants.GetProperty("variants").EnumerateArray()
                .Select(item => (item.GetProperty("id").GetString(), item.GetProperty("appearance").GetString()))
                .Should().Equal(("dark", "dark"), ("light", "light"));
        }
        finally
        {
            TestDirectory.Delete(projectRoot);
        }
    }

    [Fact]
    public void BuiltInWpfUi_ShouldOfferLightResourcesForExplicitLightSurface()
    {
        var registry = new PackRegistry(
            ComposerPackPaths.BuiltinRoot(TestRepositoryPaths.GetRepoFilePath(".")));
        var blueprint = """
            {
              "schemaVersion": "wpfdevtools.ui-blueprint.v1",
              "name": "ReadableSurface",
              "packs": [
                { "id": "core", "version": "0.1.0", "required": true, "role": "layout-pack" },
                { "id": "wpfui", "version": "0.1.0", "required": true, "role": "primary" }
              ],
              "primaryPack": "wpfui",
              "resourceVariants": { "wpfui": "light" },
              "layout": {
                "kind": "core.border",
                "properties": { "background": "#FFF8EA" },
                "slots": { "content": [{ "kind": "wpfui.button", "properties": { "text": "Readable" } }] }
              }
            }
            """;

        var validation = new BlueprintValidationService(registry).Validate(blueprint);
        var render = new UiBlueprintRenderer(registry).Render(new RenderBlueprintRequest(blueprint));

        validation.Success.Should().BeTrue();
        validation.Warnings.Should().NotContain(issue => issue.Code == "SurfaceThemeContrastRisk");
        render.RequiredResources.Should().Equal(
            "<ui:ThemesDictionary Theme=\"Light\" />",
            "<ui:ControlsDictionary />");
    }

    private static PackRegistry CreateRegistry(string projectRoot)
        => new(
            ComposerPackPaths.BuiltinRoot(TestRepositoryPaths.GetRepoFilePath(".")),
            ComposerPackPaths.ProjectLocalRoot(projectRoot));

    private static string CreateProjectWithThemePack()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), "wpfdevtools-theme-" + Guid.NewGuid().ToString("N"));
        var packRoot = Path.Combine(projectRoot, ".wpfdevtools", "packs", "nebula", "1.0.0");
        Directory.CreateDirectory(Path.Combine(packRoot, "blocks"));
        Directory.CreateDirectory(Path.Combine(packRoot, "renderers", "xaml"));
        File.WriteAllText(Path.Combine(packRoot, "pack.json"),
            """
            {
              "schemaVersion": "wpfdevtools.ui-pack.v1",
              "id": "nebula",
              "kind": "control-pack",
              "displayName": "Nebula",
              "version": "1.0.0",
              "xmlNamespaces": { "nebula": "urn:nebula" },
              "resourceSetup": {
                "defaultVariant": "dark",
                "variants": {
                  "dark": { "appearance": "dark", "applicationMergedDictionaries": ["<nebula:Theme Mode=\"Dark\" />"] },
                  "light": { "appearance": "light", "applicationMergedDictionaries": ["<nebula:Theme Mode=\"Light\" />"] }
                }
              },
              "blocks": ["nebula.surface", "nebula.control"],
              "recipes": []
            }
            """);
        File.WriteAllText(Path.Combine(packRoot, "source.lock.json"),
            """{"schemaVersion":"wpfdevtools.source-lock.v1","sources":[],"transformPolicy":{}}""");
        File.WriteAllText(Path.Combine(packRoot, "install.manifest.json"),
            """{"schemaVersion":"wpfdevtools.pack-install-manifest.v1","id":"nebula","version":"1.0.0","scope":"project-local","path":".","enabled":true}""");
        File.WriteAllText(Path.Combine(packRoot, "blocks", "surface.block.json"),
            """
            {
              "schemaVersion": "wpfdevtools.ui-block.v1",
              "kind": "nebula.surface",
              "displayName": "Surface",
              "description": "Theme-aware surface.",
              "category": "layout",
              "properties": { "background": { "type": "string", "required": true, "visualRole": "surface" } },
              "slots": { "content": { "allowedKinds": ["nebula.control"] } },
              "renderer": { "xamlTemplate": "renderers/xaml/surface.xaml.sbn" },
              "sourceHints": []
            }
            """);
        File.WriteAllText(Path.Combine(packRoot, "blocks", "control.block.json"),
            """
            {
              "schemaVersion": "wpfdevtools.ui-block.v1",
              "kind": "nebula.control",
              "displayName": "Control",
              "description": "Theme-styled control.",
              "category": "input",
              "properties": {},
              "slots": {},
              "renderer": { "xamlTemplate": "renderers/xaml/control.xaml.sbn" },
              "sourceHints": []
            }
            """);
        File.WriteAllText(Path.Combine(packRoot, "renderers", "xaml", "surface.xaml.sbn"),
            "<Border Background=\"{{background}}\">{{slot.content}}</Border>");
        File.WriteAllText(Path.Combine(packRoot, "renderers", "xaml", "control.xaml.sbn"),
            "<nebula:Control />");
        return projectRoot;
    }

    private static string Blueprint(string variant)
        => $$"""
            {
              "schemaVersion": "wpfdevtools.ui-blueprint.v1",
              "name": "ThemeProbe",
              "packs": [{ "id": "nebula", "version": "1.0.0", "required": true, "role": "primary" }],
              "primaryPack": "nebula",
              "resourceVariants": { "nebula": "{{variant}}" },
              "layout": {
                "kind": "nebula.surface",
                "properties": { "background": "#FFF8EA" },
                "slots": { "content": [{ "kind": "nebula.control" }] }
              }
            }
            """;
}
