using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Catalog;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Mcp.Server.Composer.Preview;
using WpfDevTools.Mcp.Server.Composer.Rendering;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerWpfUiEditorialCardTests
{
    [Fact]
    public void Catalog_ShouldExposeImageCapableEditorialCompositionContract()
    {
        var item = new BlockCatalogService(CreateRegistry())
            .GetCatalog(new BlockCatalogQuery(Kind: "wpfui.editorialCard"))
            .Items.Single();

        item.Description.Should().ContainEquivalentOf("image-capable")
            .And.ContainEquivalentOf("wide");
        item.AuthoringRoles.Should().Contain(["hero", "editorial-media"])
            .And.NotContain("product-tile");
        item.Properties["maxWidth"].Description.Should()
            .ContainEquivalentOf("mediaWidth")
            .And.ContainEquivalentOf("320");
        item.Properties["mediaSource"].Description.Should()
            .ContainEquivalentOf("project-owned")
            .And.ContainEquivalentOf("media slot")
            .And.ContainEquivalentOf("WPF Resource")
            .And.Contain("/Assets/hero.png")
            .And.Contain("pack://application:,,,/");
        item.Properties["mediaSource"].PreviewWarning.Should()
            .ContainEquivalentOf("projectRoot")
            .And.ContainEquivalentOf("isolated preview")
            .And.ContainEquivalentOf("final built application");
        item.Properties["mediaSource"].Type.Should().Be("string");
        item.Properties["titleFontFamily"].Default!.ToString().Should()
            .Be("Segoe UI Variable Display, Segoe UI");
        item.Properties["bodyFontFamily"].Default!.ToString().Should()
            .Be("Segoe UI Variable Text, Segoe UI");
        item.Properties["mediaAutomationName"].Required.Should().BeTrue();
        item.Slots["media"].MaxItems.Should().Be(1);
        item.Slots["media"].AllowedKinds.Should().Equal("wpfui.symbolIcon");
        item.Slots["media"].Description.Should().NotContainEquivalentOf("overlay");
        item.Slots.Keys.Should().Contain(["content", "actions"]);
        item.CompositionSkeleton!.Value.GetProperty("properties")
            .GetProperty("title").GetString().Should().Be("Featured collection");
    }

    [Fact]
    public void Renderer_ShouldAllowApplicationLocalEditorialMedia()
    {
        var result = new UiBlueprintRenderer(CreateRegistry()).Render(new RenderBlueprintRequest(Blueprint("""
            {
              "kind": "wpfui.editorialCard",
              "properties": {
                "title": "Local artwork",
                "mediaSource": "pack://application:,,,/ComposerGeneratedApp;component/Assets/hero.png",
                "mediaAutomationName": "Original local artwork"
              }
            }
            """)));

        result.Success.Should().BeTrue(result.Errors.FirstOrDefault()?.Message);
        result.Xaml.Should().Contain(
            "Source=\"pack://application:,,,/ComposerGeneratedApp;component/Assets/hero.png\"");
    }

    [Fact]
    public void Renderer_ShouldProduceAccessibleHorizontalEditorialSurface()
    {
        var result = new UiBlueprintRenderer(CreateRegistry()).Render(new RenderBlueprintRequest(Blueprint("""
            {
              "kind": "wpfui.editorialCard",
              "properties": {
                "eyebrow": "Curated", "title": "Signal Gardens", "description": "Living soundscapes",
                "mediaSource": "{Binding HeroImage}", "mediaAutomationName": "Purple garden artwork",
                "mediaBackground": "#243247", "mediaWidth": 420, "mediaHeight": 300,
                "mediaStretch": "UniformToFill", "margin": "12", "padding": "28",
                "titleFontFamily": "Heading Face", "bodyFontFamily": "Body Face"
              },
              "slots": {
                "media": [{ "kind": "wpfui.symbolIcon", "properties": { "symbol": "Image24" } }],
                "content": [{ "kind": "core.text", "properties": { "text": "Desktop collection" } }],
                "actions": [{ "kind": "wpfui.button", "properties": { "text": "Explore" } }]
              }
            }
            """)));

        result.Success.Should().BeTrue(result.Errors.FirstOrDefault()?.Message);
        result.Xaml.Should().Contain("<ui:Card Margin=\"12\" MaxWidth=\"1000000\" MinHeight=\"300\" HorizontalContentAlignment=\"Stretch\"")
            .And.Contain("<ColumnDefinition Width=\"420\"")
            .And.Contain("<Image Source=\"{Binding HeroImage}\" Stretch=\"UniformToFill\"")
            .And.Contain("AutomationProperties.Name=\"Purple garden artwork\"")
            .And.Contain("Text=\"Signal Gardens\" Appearance=\"Primary\" FontFamily=\"Heading Face\"")
            .And.Contain("Text=\"Living soundscapes\" Appearance=\"Secondary\" FontFamily=\"Body Face\"")
            .And.Contain("<ui:SymbolIcon Symbol=\"Image24\"")
            .And.Contain("<ui:Button")
            .And.Contain("Content=\"Explore\"");
        result.Xaml.IndexOf("<Viewbox", StringComparison.Ordinal).Should().BeLessThan(
            result.Xaml.IndexOf("<Image", StringComparison.Ordinal),
            "a successfully loaded image must cover the fallback symbol instead of being permanently overlaid");
    }

    [Fact]
    public void Renderer_ShouldNotReserveSpacingForOmittedOptionalSlots()
    {
        var result = new UiBlueprintRenderer(CreateRegistry()).Render(new RenderBlueprintRequest(Blueprint("""
            {
              "kind": "wpfui.editorialCard",
              "properties": { "title": "Compact editorial", "mediaAutomationName": "Editorial surface" }
            }
            """)));

        result.Success.Should().BeTrue(result.Errors.FirstOrDefault()?.Message);
        result.Xaml.Should().NotContain("Margin=\"0,16")
            .And.NotContain("Margin=\"0,20");
    }

    [Fact]
    public void Renderer_ShouldSpaceMultipleEditorialActions()
    {
        var result = new UiBlueprintRenderer(CreateRegistry()).Render(new RenderBlueprintRequest(Blueprint("""
            {
              "kind": "wpfui.editorialCard",
              "properties": { "title": "Actionable editorial", "mediaAutomationName": "Editorial surface" },
              "slots": {
                "actions": [
                  { "kind": "wpfui.button", "properties": { "text": "Explore" } },
                  { "kind": "wpfui.button", "properties": { "text": "Save" } }
                ]
              }
            }
            """)));

        result.Success.Should().BeTrue(result.Errors.FirstOrDefault()?.Message);
        (result.Xaml.Split("Margin=\"0,20,12,0\"", StringSplitOptions.None).Length - 1)
            .Should().Be(2, "spacing belongs to each rendered action rather than an empty slot container");
    }

    [Fact]
    [Trait("Category", "ComposerCompile")]
    public void Preview_ShouldCompileEditorialFallbackWithoutMediaAsset()
    {
        var blueprint = Blueprint("""
            {
              "kind": "wpfui.editorialCard",
              "properties": {
                "title": "Offline collection",
                "description": "Theme-aware media fallback",
                "mediaAutomationName": "Collection illustration"
              },
              "slots": {
                "media": [{ "kind": "wpfui.symbolIcon", "properties": { "symbol": "Image24" } }]
              }
            }
            """);

        var result = new UiBlueprintPreviewService(CreateRegistry())
            .Preview(new PreviewBlueprintRequest(blueprint, RestoreEnabled: true));

        result.Success.Should().BeTrue(string.Join(" | ", result.Diagnostics.Select(item => item.Message)));
        result.BuildSucceeded.Should().BeTrue(result.BuildOutput);
        result.Xaml.Should().NotContain(" Source=");
    }

    private static PackRegistry CreateRegistry()
        => PackRegistry.ForRepository(TestRepositoryPaths.GetRepoFilePath("."));

    private static string Blueprint(string layout)
        => $$"""
            {
              "schemaVersion": "wpfdevtools.ui-blueprint.v1",
              "name": "EditorialCard",
              "packs": [
                { "id": "core", "version": "0.1.0", "required": true, "role": "layout-pack" },
                { "id": "wpfui", "version": "0.1.0", "required": true, "role": "primary" }
              ],
              "primaryPack": "wpfui",
              "layout": {{layout}}
            }
            """;
}
