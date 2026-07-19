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

        item.Description.Should().ContainEquivalentOf("image-capable");
        item.AuthoringRoles.Should().Contain(["hero", "product-tile", "editorial-media"]);
        item.Properties["mediaSource"].Description.Should().ContainEquivalentOf("project-owned");
        item.Properties["mediaAutomationName"].Required.Should().BeTrue();
        item.Slots["media"].MaxItems.Should().Be(1);
        item.Slots["media"].AllowedKinds.Should().Equal("wpfui.symbolIcon");
        item.Slots.Keys.Should().Contain(["content", "actions"]);
        item.CompositionSkeleton!.Value.GetProperty("properties")
            .GetProperty("title").GetString().Should().Be("Featured collection");
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
                "mediaStretch": "UniformToFill", "margin": "12", "padding": "28"
              },
              "slots": {
                "media": [{ "kind": "wpfui.symbolIcon", "properties": { "symbol": "Image24" } }],
                "content": [{ "kind": "core.text", "properties": { "text": "Desktop collection" } }],
                "actions": [{ "kind": "wpfui.button", "properties": { "text": "Explore" } }]
              }
            }
            """)));

        result.Success.Should().BeTrue(result.Errors.FirstOrDefault()?.Message);
        result.Xaml.Should().Contain("<ui:Card Margin=\"12\"")
            .And.Contain("<ColumnDefinition Width=\"420\"")
            .And.Contain("<Image Source=\"{Binding HeroImage}\" Stretch=\"UniformToFill\"")
            .And.Contain("AutomationProperties.Name=\"Purple garden artwork\"")
            .And.Contain("Text=\"Signal Gardens\"")
            .And.Contain("<ui:SymbolIcon Symbol=\"Image24\"")
            .And.Contain("<ui:Button")
            .And.Contain("Content=\"Explore\"");
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
