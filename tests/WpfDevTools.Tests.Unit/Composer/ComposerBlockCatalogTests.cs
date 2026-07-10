using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Catalog;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Mcp.Server.McpTools;
using WpfDevTools.Tests.Unit.TestSupport;
using Xunit;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerBlockCatalogTests
{
    [Fact]
    public void BlockCatalog_ShouldExposeWpfUiBlocksWithDtoFields()
    {
        var catalog = CreateCatalog();

        var result = catalog.GetCatalog(new BlockCatalogQuery());

        result.Items.Should().HaveCount(15);
        var button = result.Items.Single(item => item.Kind == "wpfui.button");
        button.PackId.Should().Be("wpfui");
        button.PackVersion.Should().Be("0.1.0");
        button.DisplayName.Should().Be("Button");
        button.Category.Should().Be("input");
        button.Properties.Keys.Should().Contain(["appearance", "text"]);
        button.Slots.Keys.Should().Contain("icon");
        button.AllowedKinds.Should().Contain("wpfui.symbolIcon");
        button.RendererAvailable.Should().BeTrue();
        button.SourceHintSummary.Should().Contain("src/Wpf.Ui/Controls/Button/Button.cs");
    }

    [Fact]
    public void BlockCatalog_ShouldFilterByPackCategoryKindPrefixComposableAndDetail()
    {
        var catalog = CreateCatalog();

        var filtered = catalog.GetCatalog(new BlockCatalogQuery(
            PackIds: ["wpfui"],
            Category: "navigation",
            KindPrefix: "wpfui.navigation",
            ComposableOnly: true,
            Kind: null));
        var detail = catalog.GetCatalog(new BlockCatalogQuery(Kind: "wpfui.navigationViewItem"));

        filtered.Items.Select(item => item.Kind).Should()
            .BeEquivalentTo(
                "wpfui.navigationView",
                "wpfui.navigationViewDemo",
                "wpfui.navigationViewItem",
                "wpfui.navigationViewItemSeparator");
        detail.Items.Should().ContainSingle(item => item.Kind == "wpfui.navigationViewItem");
    }

    [Theory]
    [InlineData("wpfui.navigationView", "items", "wpfui.navigationViewItem")]
    [InlineData("wpfui.navigationView", "items", "wpfui.navigationViewItemSeparator")]
    [InlineData("wpfui.navigationViewItem", "icon", "wpfui.symbolIcon")]
    [InlineData("wpfui.tabView", "items", "wpfui.tabViewItem")]
    [InlineData("wpfui.contentDialog", "actions", "wpfui.button")]
    [InlineData("wpfui.fluentWindow", "titleBar", "wpfui.titleBar")]
    [InlineData("wpfui.snackbar", "actions", "wpfui.button")]
    public void BlockCatalog_ShouldExposeSlotCompositionRules(string kind, string slot, string allowedKind)
    {
        var catalog = CreateCatalog();

        var item = catalog.GetCatalog(new BlockCatalogQuery(Kind: kind)).Items.Single();

        item.Slots[slot].AllowedKinds.Should().Contain(allowedKind);
    }

    [Fact]
    public void BlockCatalog_ShouldAvoidLongSourceCodeAndOptionalSurfaceBlocks()
    {
        var catalog = CreateCatalog();

        var result = catalog.GetCatalog(new BlockCatalogQuery());
        var serialized = System.Text.Json.JsonSerializer.Serialize(result);

        serialized.Should().NotContain("namespace Wpf.Ui");
        serialized.Should().NotContain("public class");
        result.Items.Select(item => item.Kind).Should().NotContain([
            "wpfui.calendar",
            "wpfui.gallery",
            "wpfui.syntaxHighlight",
            "wpfui.tray",
            "wpfui.template"
        ]);
    }

    [Fact]
    public async Task GetUiBlockCatalogTool_ShouldReturnStructuredCatalog()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var result = await UiComposerMcpTools.GetUiBlockCatalog(
                packIds: ["wpfui"],
                kind: "wpfui.button",
                localAppDataRoot: tempRoot,
                cancellationToken: CancellationToken.None);

            result.IsError.Should().BeFalse();
            var payload = result.StructuredContent!.Value;
            payload.GetProperty("success").GetBoolean().Should().BeTrue();
            payload.GetProperty("itemCount").GetInt32().Should().Be(1);
            payload.GetProperty("items")[0].GetProperty("kind").GetString().Should().Be("wpfui.button");
            payload.GetProperty("items")[0].GetProperty("allowedKinds").EnumerateArray()
                .Select(item => item.GetString())
                .Should().Contain("wpfui.symbolIcon");
            payload.GetProperty("compositionExampleCount").GetInt32().Should().Be(1);
            var example = payload.GetProperty("compositionExamples")[0];
            example.GetProperty("id").GetString().Should().Be("core.stack.multiple-cards");
            example.GetProperty("fragment").GetProperty("kind").GetString().Should().Be("stack");
            example.GetProperty("fragment").GetProperty("slots").GetProperty("stack")
                .EnumerateArray()
                .Should().HaveCount(2)
                .And.OnlyContain(item => item.GetProperty("kind").GetString() == "wpfui.card");

            var blueprintJson = $$"""
                {
                  "schemaVersion": "wpfdevtools.ui-blueprint.v1",
                  "name": "CatalogCompositionExample",
                  "packs": [{ "id": "wpfui", "version": "0.1.0", "required": true, "role": "primary" }],
                  "primaryPack": "wpfui",
                  "layout": {
                    "kind": "wpfui.card",
                    "slots": { "content": [{{example.GetProperty("fragment").GetRawText()}}] }
                  }
                }
                """;
            var validation = await UiComposerMcpTools.ValidateUiBlueprint(
                blueprintJson,
                localAppDataRoot: tempRoot,
                cancellationToken: CancellationToken.None);
            var validationPayload = validation.StructuredContent!.Value;
            validationPayload.GetProperty("valid").GetBoolean().Should().BeTrue(validationPayload.GetRawText());

            var render = await UiComposerMcpTools.RenderUiBlueprint(
                blueprintJson,
                targetPath: "CatalogCompositionExample.xaml",
                localAppDataRoot: tempRoot,
                cancellationToken: CancellationToken.None);
            var renderPayload = render.StructuredContent!.Value;
            renderPayload.GetProperty("success").GetBoolean().Should().BeTrue(renderPayload.GetRawText());
            renderPayload.GetProperty("xaml").GetString().Should()
                .Contain("<ui:TextBlock Text=\"First card\"")
                .And.Contain("<ui:TextBlock Text=\"Second card\"");

            var projectRoot = Path.Combine(tempRoot, "project");
            CreatePartiallyRenderableWpfUiOverride(projectRoot);
            var overridden = await UiComposerMcpTools.GetUiBlockCatalog(
                packIds: ["wpfui"],
                projectRoot: projectRoot,
                localAppDataRoot: tempRoot,
                cancellationToken: CancellationToken.None);
            overridden.StructuredContent!.Value.GetProperty("compositionExampleCount").GetInt32().Should().Be(0);
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    private static BlockCatalogService CreateCatalog()
    {
        var registry = PackRegistry.ForRepository(TestRepositoryPaths.GetRepoFilePath("."));
        return new BlockCatalogService(registry);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "wpfdevtools-composer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void CreatePartiallyRenderableWpfUiOverride(string projectRoot)
    {
        var root = Path.Combine(projectRoot, ".wpfdevtools", "packs", "wpfui", "9.9.9");
        Directory.CreateDirectory(Path.Combine(root, "blocks"));
        Directory.CreateDirectory(Path.Combine(root, "renderers", "xaml"));
        Directory.CreateDirectory(Path.Combine(root, "recipes"));
        Directory.CreateDirectory(Path.Combine(root, "examples"));
        File.WriteAllText(Path.Combine(root, "pack.json"),
            """{"schemaVersion":"wpfdevtools.ui-pack.v1","id":"wpfui","displayName":"Override","version":"9.9.9","blocks":["wpfui.card","wpfui.textBlock"],"recipes":[]}""");
        File.WriteAllText(Path.Combine(root, "source.lock.json"),
            """{"schemaVersion":"wpfdevtools.source-lock.v1","sources":[{"name":"Override","url":"https://example.invalid/override","version":"9.9.9","paths":["src"]}],"transformPolicy":{}}""");
        File.WriteAllText(Path.Combine(root, "blocks", "card.block.json"),
            """{"schemaVersion":"wpfdevtools.ui-block.v1","kind":"wpfui.card","displayName":"Card","category":"container","properties":{},"slots":{"content":{"allowedKinds":["stack"]}},"renderer":{"xamlTemplate":"renderers/xaml/card.xaml.sbn"},"sourceHints":[]}""");
        File.WriteAllText(Path.Combine(root, "blocks", "textBlock.block.json"),
            """{"schemaVersion":"wpfdevtools.ui-block.v1","kind":"wpfui.textBlock","displayName":"Text Block","category":"display","properties":{"text":{"type":"string","required":false,"default":"Text"}},"slots":{},"renderer":{"xamlTemplate":"renderers/xaml/textBlock.xaml.sbn"},"sourceHints":[]}""");
        File.WriteAllText(Path.Combine(root, "renderers", "xaml", "card.xaml.sbn"), "<ui:Card>{{slot.content}}</ui:Card>");
        var escapedRoot = root.Replace("\\", "\\\\");
        File.WriteAllText(Path.Combine(root, "install.manifest.json"),
            $$"""{"schemaVersion":"wpfdevtools.pack-install-manifest.v1","id":"wpfui","version":"9.9.9","scope":"project","path":"{{escapedRoot}}","enabled":true}""");
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
