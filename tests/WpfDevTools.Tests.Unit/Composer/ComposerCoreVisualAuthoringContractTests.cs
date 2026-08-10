using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Catalog;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerCoreVisualAuthoringContractTests
{
    [Fact]
    public void CorePack_ShouldDescribeSelectedMediaAndEdgeControlSafeAreas()
    {
        var catalog = new BlockCatalogService(CreateRegistry());

        var border = catalog.GetCatalog(new BlockCatalogQuery(Kind: "core.border")).Items.Single();
        var scrollViewer = catalog.GetCatalog(new BlockCatalogQuery(Kind: "core.scrollViewer")).Items.Single();

        border.Description.Should().Contain("selected").And.Contain("borderBrush");
        scrollViewer.Description.Should().Contain("trailing gutter").And.Contain("control footprint");
    }

    [Fact]
    public void CorePack_ShouldDescribeHeadingAndTrailingActionAlignment()
    {
        var gridCell = new BlockCatalogService(CreateRegistry())
            .GetCatalog(new BlockCatalogQuery(Kind: "core.gridCell")).Items.Single();

        gridCell.Description.Should().Contain("heading").And.Contain("vertical alignment");
    }

    private static PackRegistry CreateRegistry()
        => PackRegistry.ForRepository(TestRepositoryPaths.GetRepoFilePath("."));
}
