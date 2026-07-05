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

        result.Items.Should().HaveCount(13);
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
            .BeEquivalentTo("wpfui.navigationView", "wpfui.navigationViewItem");
        detail.Items.Should().ContainSingle(item => item.Kind == "wpfui.navigationViewItem");
    }

    [Theory]
    [InlineData("wpfui.navigationView", "items", "wpfui.navigationViewItem")]
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
        result.Items.Select(item => item.Kind).Should().NotContain("wpfui.calendar");
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

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
