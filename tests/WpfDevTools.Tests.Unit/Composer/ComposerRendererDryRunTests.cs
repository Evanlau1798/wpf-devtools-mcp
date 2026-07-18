using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Contracts;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Mcp.Server.Composer.Rendering;
using WpfDevTools.Mcp.Server.McpTools;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerRendererDryRunTests
{
    [Fact]
    public void RendererTemplateLoader_ShouldResolveTokensAndUseCache()
    {
        var loader = new RendererTemplateLoader(CreateRegistry());

        var first = loader.Load("wpfui.button", DeclaredWpfUiPack());
        var second = loader.Load("wpfui.button", DeclaredWpfUiPack());

        first.Success.Should().BeTrue();
        first.Template!.Tokens.Should().Contain(["text", "appearance", "slot.icon"]);
        first.Template.TemplatePath.Should().EndWith("button.xaml.sbn");
        first.FromCache.Should().BeFalse();
        second.FromCache.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(GoldenBlueprints))]
    public void RenderBlueprint_ShouldRenderGoldenBlueprints(string blueprintJson, string expectedSnippet)
    {
        var renderer = new UiBlueprintRenderer(CreateRegistry());

        var result = renderer.Render(new RenderBlueprintRequest(blueprintJson));

        result.Success.Should().BeTrue();
        result.Valid.Should().BeTrue();
        result.DryRun.Should().BeTrue();
        result.Xaml.Should().Contain(expectedSnippet);
        result.FilePlan.WouldWriteFiles.Should().BeFalse();
        result.RequiredNuGetPackages.Should().Contain(package => package.Id == "WPF-UI");
        result.RequiredResources.Should().Contain(resource => resource.Contains("ThemesDictionary", StringComparison.Ordinal));
        result.RequiredResources.Should().Contain(resource => resource.Contains("ControlsDictionary", StringComparison.Ordinal));
    }

    [Fact]
    public void RenderBlueprint_ShouldPreservePackResourceDeclarationOrder()
    {
        var renderer = new UiBlueprintRenderer(CreateRegistry());

        var result = renderer.Render(new RenderBlueprintRequest(Blueprint(
            """{ "kind": "wpfui.tabView", "slots": { "items": [{ "kind": "wpfui.tabViewItem" }] } }""")));

        result.Success.Should().BeTrue();
        result.RequiredResources.Should().Equal(
            "<ui:ThemesDictionary Theme=\"Dark\" />",
            "<ui:ControlsDictionary />");
    }

    [Fact]
    public void RenderBlueprint_ShouldRenderButtonIconSlotWithPropertyElement()
    {
        var renderer = new UiBlueprintRenderer(CreateRegistry());

        var result = renderer.Render(new RenderBlueprintRequest(Blueprint("""
            {
              "kind": "wpfui.button",
              "properties": { "text": "Save" },
              "slots": { "icon": [{ "kind": "wpfui.symbolIcon", "properties": { "symbol": "Save24" } }] }
            }
            """)));

        result.Success.Should().BeTrue();
        result.Xaml.Should().Contain("<ui:Button.Icon>");
        result.Xaml.Should().Contain("<ui:SymbolIcon Symbol=\"Save24\" />");
    }

    [Fact]
    public void RenderBlueprint_ShouldOmitEmptyPropertyElementSlot()
    {
        var renderer = new UiBlueprintRenderer(CreateRegistry());

        var result = renderer.Render(new RenderBlueprintRequest(Blueprint("""
            {
              "kind": "wpfui.button",
              "properties": { "text": "Save" }
            }
            """)));

        result.Success.Should().BeTrue();
        result.Xaml.Should().NotContain("<ui:Button.Icon>");
    }

    [Fact]
    public void RenderBlueprint_ShouldRenderDataGridColumnsAsPropertyElement()
    {
        var renderer = new UiBlueprintRenderer(CreateRegistry());

        var result = renderer.Render(new RenderBlueprintRequest(Blueprint("""
            {
              "kind": "wpfui.dataGrid",
              "properties": { "itemsSource": "{Binding Rows}" },
              "slots": { "columns": [{ "kind": "core.template" }] }
            }
            """)));

        result.Success.Should().BeTrue();
        result.Xaml.Should().Contain("<ui:DataGrid.Columns>");
        result.Xaml.Should().Contain("<!-- template -->");
    }

    [Fact]
    public void RenderBlueprint_ShouldUseThemeNeutralNavigationViewContentOverlay()
    {
        var renderer = new UiBlueprintRenderer(CreateRegistry());

        var result = renderer.Render(new RenderBlueprintRequest(Blueprint("""
            {
              "kind": "wpfui.navigationView",
              "slots": { "content": [{ "kind": "wpfui.card" }] }
            }
            """)));

        result.Success.Should().BeTrue();
        result.Xaml.Should().Contain("<ui:NavigationView PaneDisplayMode=\"Left\"");
        result.Xaml.Should().Contain("<ui:AutoSuggestBox Visibility=\"Collapsed\" />");
        result.Xaml.Should().Contain("<ui:NavigationView.ContentOverlay>");
        result.Xaml.Should().Contain("<Grid><ui:Card");
        result.Xaml.Should().NotContainAny("PlaceholderText=", "Width=\"386\"", "#252B33", "#36E0A6");
        result.Xaml.Should().Contain("<ui:Card");
    }

    [Fact]
    public void RenderBlueprint_ShouldRenderWpfUiCardWithoutUnsupportedAppearance()
    {
        var renderer = new UiBlueprintRenderer(CreateRegistry());

        var result = renderer.Render(new RenderBlueprintRequest(Blueprint("""
            {
              "kind": "wpfui.card",
              "slots": { "content": [{ "kind": "core.text", "properties": { "text": "Ready" } }] }
            }
            """)));

        result.Success.Should().BeTrue();
        result.Xaml.Should().Contain("<ui:Card");
        result.Xaml.Should().NotContain("Appearance=");
    }

    [Fact]
    public void RenderBlueprint_ShouldPlaceFluentWindowTitleBarInContentTree()
    {
        var renderer = new UiBlueprintRenderer(CreateRegistry());

        var result = renderer.Render(new RenderBlueprintRequest(Blueprint("""
            {
              "kind": "wpfui.fluentWindow",
              "slots": {
                "titleBar": [{ "kind": "wpfui.titleBar", "properties": { "title": "Shell" } }],
                "content": [{ "kind": "wpfui.navigationView" }]
              }
            }
            """)));

        result.Success.Should().BeTrue();
        result.Xaml.Should().NotContain("FluentWindow.TitleBar");
        result.Xaml.Should().Contain("<Grid>");
        result.Xaml.Should().Contain("Grid.Row=\"0\"");
        result.Xaml.Should().Contain("<ui:TitleBar Title=\"Shell\" Height=\"56\"");
        result.Xaml.Should().NotContain("<ui:TitleBar.Icon>");
        result.Xaml.Should().Contain("Grid.Row=\"1\"");
    }

    [Fact]
    public void RenderBlueprint_ShouldReturnFilePlanWithoutWriting()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var targetPath = Path.Combine(tempRoot, "GeneratedView.xaml");
            var renderer = new UiBlueprintRenderer(CreateRegistry());

            var result = renderer.Render(new RenderBlueprintRequest(
                Blueprint("""{ "kind": "wpfui.textBlock", "properties": { "text": "Preview" } }"""),
                targetPath));

            result.Success.Should().BeTrue();
            result.FilePlan.TargetPath.Should().Be(Path.GetFullPath(targetPath));
            result.FilePlan.WouldWriteFiles.Should().BeFalse();
            File.Exists(targetPath).Should().BeFalse();
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void RenderBlueprint_ShouldReturnLocatedErrors()
    {
        var renderer = new UiBlueprintRenderer(CreateRegistry());

        var result = renderer.Render(new RenderBlueprintRequest(Blueprint("""
            {
              "kind": "wpfui.card",
              "slots": { "missing": [{ "kind": "wpfui.button" }] }
            }
            """)));

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(error => error.JsonPath == "$.layout.slots.missing"
            && error.Code == "UnknownSlot");
    }

    [Fact]
    public async Task RenderUiBlueprintTool_ShouldReturnStructuredDryRun()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var result = await UiComposerMcpTools.RenderUiBlueprint(
                Blueprint("""{ "kind": "wpfui.titleBar", "slots": { "actions": [{ "kind": "wpfui.button" }] } }"""),
                localAppDataRoot: tempRoot,
                cancellationToken: CancellationToken.None);

            result.IsError.Should().BeFalse();
            var payload = result.StructuredContent!.Value;
            payload.GetProperty("success").GetBoolean().Should().BeTrue();
            payload.GetProperty("dryRun").GetBoolean().Should().BeTrue();
            payload.GetProperty("xaml").GetString().Should().Contain("<ui:TitleBar.TrailingContent>");
            payload.GetProperty("filePlan").GetProperty("wouldWriteFiles").GetBoolean().Should().BeFalse();
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public async Task RenderUiBlueprintTool_WithProjectRoot_ShouldResolveRelativeTargetInsideProjectRoot()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var result = await UiComposerMcpTools.RenderUiBlueprint(
                Blueprint("""{ "kind": "wpfui.textBlock", "properties": { "text": "Preview" } }"""),
                targetPath: "Views/GeneratedView.xaml",
                projectRoot: tempRoot,
                cancellationToken: CancellationToken.None);

            result.IsError.Should().BeFalse();
            var payload = result.StructuredContent!.Value;
            payload.GetProperty("filePlan").GetProperty("targetPath").GetString()
                .Should().Be(Path.GetFullPath(Path.Combine(tempRoot, "Views", "GeneratedView.xaml")));
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    public static TheoryData<string, string> GoldenBlueprints()
        => new()
        {
            { Blueprint("""{ "kind": "wpfui.button", "slots": { "icon": [{ "kind": "wpfui.symbolIcon" }] } }"""), "<ui:Button" },
            { Blueprint("""{ "kind": "wpfui.fluentWindow", "slots": { "titleBar": [{ "kind": "wpfui.titleBar" }], "content": [{ "kind": "wpfui.navigationView" }] } }"""), "<ui:FluentWindow" },
            { Blueprint("""{ "kind": "wpfui.navigationView", "slots": { "items": [{ "kind": "wpfui.navigationViewItem", "slots": { "icon": [{ "kind": "wpfui.symbolIcon" }] } }] } }"""), "<ui:NavigationViewItem" },
            { Blueprint("""{ "kind": "wpfui.tabView", "slots": { "items": [{ "kind": "wpfui.tabViewItem" }] } }"""), "<ui:TabViewItem" },
            { Blueprint("""{ "kind": "wpfui.dataGrid", "slots": { "columns": [{ "kind": "core.template" }] } }"""), "<ui:DataGrid.Columns>" },
            { Blueprint("""{ "kind": "wpfui.navigationViewDemo" }"""), "NavigationView Header" },
            { Blueprint("""{ "kind": "wpfui.card", "slots": { "content": [{ "kind": "core.text", "properties": { "text": "Plain preview" } }] } }"""), "Plain preview" },
            { Blueprint("""{ "kind": "wpfui.card", "slots": { "content": [{ "kind": "core.stack", "slots": { "children": [{ "kind": "core.text", "properties": { "text": "Line 1" } }, { "kind": "core.template" }] } }] } }"""), "<StackPanel" }
        };

    private static PackRegistry CreateRegistry()
        => PackRegistry.ForRepository(TestRepositoryPaths.GetRepoFilePath("."));

    private static string Blueprint(string layoutJson)
        => $$"""
            {
              "schemaVersion": "wpfdevtools.ui-blueprint.v1",
              "name": "GeneratedView",
              "packs": [
                { "id": "core", "version": "0.1.0", "required": true, "role": "layout-pack" },
                { "id": "wpfui", "version": "0.1.0", "required": true, "role": "primary" }
              ],
              "primaryPack": "wpfui",
              "layout": {{layoutJson}}
            }
            """;

    private static ComposerPackReference[] DeclaredWpfUiPack()
        => [new() { Id = "wpfui", Version = "0.1.0", Required = true }];

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
