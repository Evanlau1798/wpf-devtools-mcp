using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Catalog;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Mcp.Server.Composer.Rendering;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerWpfUiVisualFoundationTests
{
    [Fact]
    public void WpfUiCatalog_ShouldExposeVisualPropertiesAndControlBlocks()
    {
        var items = new BlockCatalogService(CreateRegistry())
            .GetCatalog(new BlockCatalogQuery(PackIds: ["wpfui"]))
            .Items;

        items.Should().HaveCount(18);
        items.Select(item => item.Kind).Should().Contain([
            "wpfui.numberBox",
            "wpfui.progressBar",
            "wpfui.progressRing",
            "wpfui.toggleSwitch"
        ]);
        var fontSize = items.Single(item => item.Kind == "wpfui.textBlock").Properties["fontSize"];
        fontSize.Minimum.Should().Be(1);
        fontSize.Maximum.Should().Be(200);
        var margin = items.Single(item => item.Kind == "wpfui.button").Properties["margin"];
        margin.Format.Should().Be("thickness");
        var progressBarOrientation = items.Single(item => item.Kind == "wpfui.progressBar").Properties["orientation"];
        progressBarOrientation.AllowedValues.Should().Equal("Horizontal");
    }

    [Fact]
    public void WpfUiCatalog_ShouldExposeSupportedTabContractsAndExcludeHostBackedControls()
    {
        var items = new BlockCatalogService(CreateRegistry())
            .GetCatalog(new BlockCatalogQuery(PackIds: ["wpfui"]))
            .Items;

        items.Select(item => item.Kind).Should().NotContain([
            "wpfui.contentDialog",
            "wpfui.snackbar"
        ]);
        var tabView = items.Single(item => item.Kind == "wpfui.tabView");
        var tabViewItem = items.Single(item => item.Kind == "wpfui.tabViewItem");
        tabViewItem.Properties.Should().NotContainKey("isClosable");
        tabView.Description.Should().ContainEquivalentOf("themed");
        tabViewItem.Description.Should().ContainEquivalentOf("themed");
    }

    [Theory]
    [InlineData("wpfui.button")]
    [InlineData("wpfui.card")]
    [InlineData("wpfui.fluentWindow")]
    [InlineData("wpfui.numberBox")]
    [InlineData("wpfui.progressBar")]
    [InlineData("wpfui.progressRing")]
    [InlineData("wpfui.symbolIcon")]
    [InlineData("wpfui.titleBar")]
    [InlineData("wpfui.toggleSwitch")]
    public void WpfUiCatalog_ShouldDescribeHighValueCompositionContracts(string kind)
    {
        var item = new BlockCatalogService(CreateRegistry())
            .GetCatalog(new BlockCatalogQuery(Kind: kind))
            .Items.Single();

        item.Properties.Should().OnlyContain(
            property => !string.IsNullOrWhiteSpace(property.Value.Description),
            $"{kind} properties should explain their generated XAML effect");
        if (item.Slots.Count > 0)
        {
            item.Slots.Should().OnlyContain(
                slot => !string.IsNullOrWhiteSpace(slot.Value.Description),
                $"{kind} slots should explain their composition role");
        }
    }

    [Fact]
    public void WpfUiRenderer_ShouldApplyVisualFoundationProperties()
    {
        var result = Render("""
            {
              "kind": "wpfui.card",
              "properties": {
                "padding": "24", "margin": "12", "maxWidth": 720, "minHeight": 180,
                "horizontalContentAlignment": "Stretch", "verticalContentAlignment": "Center"
              },
              "slots": {
                "header": [{
                  "kind": "wpfui.textBlock",
                  "properties": {
                    "text": "Telemetry", "fontSize": 28, "fontWeight": "SemiBold",
                    "textWrapping": "Wrap", "textAlignment": "Left", "margin": "0,0,0,12"
                  }
                }],
                "actions": [{
                  "kind": "wpfui.button",
                  "properties": { "text": "Inspect", "margin": "8", "minWidth": 120, "horizontalAlignment": "Right" }
                }]
              }
            }
            """);

        result.Success.Should().BeTrue(result.Errors.FirstOrDefault()?.Message);
        result.Xaml.Should().Contain("<ui:Card Margin=\"12\" MaxWidth=\"720\" MinHeight=\"180\"")
            .And.Contain("<Border Padding=\"24\"")
            .And.Contain("HorizontalAlignment=\"Stretch\" VerticalAlignment=\"Center\"")
            .And.Contain("FontSize=\"28\" FontWeight=\"SemiBold\"")
            .And.Contain("Margin=\"8\" MinWidth=\"120\" HorizontalAlignment=\"Right\"");
    }

    [Fact]
    public void WpfUiRenderer_ShouldRenderPackDefinedInputAndProgressControls()
    {
        var result = Render("""
            {
              "kind": "core.stack",
              "properties": { "spacing": "4" },
              "slots": { "children": [
                { "kind": "wpfui.numberBox", "properties": { "value": 42, "minimum": 0, "maximum": 100, "smallChange": 5 } },
                { "kind": "wpfui.toggleSwitch", "properties": { "isChecked": true, "offContent": "Off", "onContent": "On", "labelPosition": "Right" } },
                { "kind": "wpfui.progressBar", "properties": { "value": 65, "minimum": 0, "maximum": 100, "isIndeterminate": false, "orientation": "Horizontal", "width": 240, "height": 4 } },
                { "kind": "wpfui.progressRing", "properties": { "progress": 65, "isIndeterminate": false, "size": 32 } }
              ] }
            }
            """);

        result.Success.Should().BeTrue(result.Errors.FirstOrDefault()?.Message);
        result.Xaml.Should().Contain("<ui:NumberBox Value=\"42\" Minimum=\"0\" Maximum=\"100\" SmallChange=\"5\"")
            .And.Contain("<ui:ToggleSwitch IsChecked=\"true\" OffContent=\"Off\" OnContent=\"On\" LabelPosition=\"Right\"")
            .And.Contain("<ProgressBar Value=\"65\" Minimum=\"0\" Maximum=\"100\" IsIndeterminate=\"false\" Orientation=\"Horizontal\" Width=\"240\" Height=\"4\"")
            .And.Contain("<ui:ProgressRing Progress=\"65\" IsIndeterminate=\"false\" Width=\"32\" Height=\"32\"");
    }

    [Fact]
    public void WpfUiTitleBar_ShouldUseOptionalPackIconWithoutFixedArtwork()
    {
        var withoutIcon = Render("""{ "kind": "wpfui.titleBar", "properties": { "title": "Studio" } }""");
        var withIcon = Render("""
            {
              "kind": "wpfui.titleBar",
              "properties": { "title": "Studio", "height": 56, "padding": "16,0" },
              "slots": { "icon": [{ "kind": "wpfui.symbolIcon", "properties": { "symbol": "Apps24" } }] }
            }
            """);

        withoutIcon.Success.Should().BeTrue();
        withoutIcon.Xaml.Should().NotContain("#29B6F6").And.NotContain("#1E88E5").And.NotContain("RectangleGeometry");
        withIcon.Success.Should().BeTrue(withIcon.Errors.FirstOrDefault()?.Message);
        withIcon.Xaml.Should().Contain("<ui:TitleBar Title=\"Studio\" Height=\"56\" Padding=\"16,0\"")
            .And.Contain("<ui:SymbolIcon Symbol=\"Apps24\"");
    }

    private static RenderBlueprintResult Render(string layout)
        => new UiBlueprintRenderer(CreateRegistry()).Render(new RenderBlueprintRequest(Blueprint(layout)));

    private static PackRegistry CreateRegistry()
        => PackRegistry.ForRepository(TestRepositoryPaths.GetRepoFilePath("."));

    private static string Blueprint(string layout)
        => $$"""
            {
              "schemaVersion": "wpfdevtools.ui-blueprint.v1",
              "name": "WpfUiVisualFoundation",
              "packs": [
                { "id": "core", "version": "0.1.0", "required": true, "role": "layout-pack" },
                { "id": "wpfui", "version": "0.1.0", "required": true, "role": "primary" }
              ],
              "primaryPack": "wpfui",
              "layout": {{layout}}
            }
            """;
}
