using System.Windows.Markup;
using System.Windows.Controls;
using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Mcp.Server.Composer.Rendering;
using WpfDevTools.Tests.Unit.TestSupport;
using WpfUiAutoSuggestBox = Wpf.Ui.Controls.AutoSuggestBox;

namespace WpfDevTools.Tests.Unit.Composer;

[Collection("WPF")]
public sealed class ComposerWpfUiAutoSuggestBoxTests
{
    [StaFact]
    public void Renderer_ShouldLoadSearchInputThroughRealWpfUi()
    {
        var registry = PackRegistry.ForRepository(TestRepositoryPaths.GetRepoFilePath("."));
        var result = new UiBlueprintRenderer(registry).Render(new RenderBlueprintRequest("""
            {
              "schemaVersion": "wpfdevtools.ui-blueprint.v1",
              "name": "TitleBarSearchInput",
              "packs": [{ "id": "wpfui", "version": "0.1.0", "required": true, "role": "primary" }],
              "primaryPack": "wpfui",
              "layout": {
                "kind": "wpfui.titleBar",
                "properties": { "title": "Catalogue" },
                "slots": {
                  "actions": [{
                    "kind": "wpfui.autoSuggestBox",
                    "properties": {
                      "placeholderText": "Search the catalogue",
                      "text": "signal",
                      "minWidth": 320,
                      "margin": "8",
                      "clearButtonEnabled": true
                    },
                    "slots": {
                      "icon": [{ "kind": "wpfui.symbolIcon", "properties": { "symbol": "Search24" } }]
                    }
                  }]
                }
              }
            }
            """));

        result.Success.Should().BeTrue(string.Join(Environment.NewLine, result.Errors.Select(error => error.Message)));
        var titleBar = XamlReader.Parse(result.Xaml).Should().BeOfType<Wpf.Ui.Controls.TitleBar>().Subject;
        var actions = titleBar.TrailingContent.Should().BeOfType<StackPanel>().Which;
        actions.Children.Count.Should().Be(1);
        var search = actions.Children[0].Should().BeOfType<WpfUiAutoSuggestBox>().Subject;
        search.PlaceholderText.Should().Be("Search the catalogue");
        search.Text.Should().Be("signal");
        search.MinWidth.Should().Be(320);
        search.ClearButtonEnabled.Should().BeTrue();
    }

    [Fact]
    public void Catalog_ShouldAllowSearchInputInTitleBarActions()
    {
        var registry = PackRegistry.ForRepository(TestRepositoryPaths.GetRepoFilePath("."));
        var titleBar = ComposerPackLoader.Load(
                registry.ListPacks().Packs.Single(pack => pack.Id == "wpfui").RootPath)
            .Blocks.Single(block => block.Kind == "wpfui.titleBar");

        titleBar.Slots["actions"].AllowedKinds.Should()
            .Contain(["wpfui.button", "wpfui.autoSuggestBox"]);
    }
}
