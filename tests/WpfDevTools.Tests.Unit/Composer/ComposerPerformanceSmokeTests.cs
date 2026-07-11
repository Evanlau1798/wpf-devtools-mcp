using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer;
using WpfDevTools.Mcp.Server.Composer.Blueprints;
using WpfDevTools.Mcp.Server.Composer.Catalog;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Mcp.Server.Composer.Rendering;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerPerformanceSmokeTests
{
    [Fact]
    public void ComposerPerformanceTargets_ShouldExposePhaseEightBaselines()
    {
        ComposerPerformanceTargets.PackRegistryLoad.Should().BePositive();
        ComposerPerformanceTargets.BlockCatalogQuery.Should().BePositive();
        ComposerPerformanceTargets.BlueprintValidation.Should().BePositive();
        ComposerPerformanceTargets.RendererDryRun.Should().BePositive();
        ComposerPerformanceTargets.PreviewSmoke.Should().BePositive();
        ComposerPerformanceTargets.MaxBlueprintNodeCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void PackRegistryLoadSmoke_ShouldResolveDefaultPack()
    {
        var result = CreateRegistry().ListPacks();

        result.Packs.Should().Contain(pack => pack.Id == "wpfui" && pack.Version == "0.1.0");
    }

    [Fact]
    public void BlockCatalogQuerySmoke_ShouldResolveDefaultBlock()
    {
        var catalog = new BlockCatalogService(CreateRegistry());

        var result = catalog.GetCatalog(new BlockCatalogQuery(Kind: "wpfui.button"));

        result.Items.Should().ContainSingle(item => item.Kind == "wpfui.button");
    }

    [Fact]
    public void BlueprintValidationSmoke_ShouldAcceptNavigationShell()
    {
        var validator = new BlueprintValidationService(CreateRegistry());

        var result = validator.Validate(NavigationShellBlueprint());

        result.Success.Should().BeTrue();
    }

    [Fact]
    public void RendererDryRunSmoke_ShouldRenderNavigationShell()
    {
        var renderer = new UiBlueprintRenderer(CreateRegistry());

        var result = renderer.Render(new RenderBlueprintRequest(NavigationShellBlueprint()));

        result.Success.Should().BeTrue();
        result.DryRun.Should().BeTrue();
    }

    [Fact]
    public void ValidateBlueprint_ShouldRejectBlueprintsAboveNodeLimit()
    {
        var validator = new BlueprintValidationService(CreateRegistry());
        var blueprint = LargeBlueprint(ComposerPerformanceTargets.MaxBlueprintNodeCount + 1);

        var result = validator.Validate(blueprint);

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle(error => error.Code == "BlueprintTooLarge")
            .Which.JsonPath.Should().Be("$.layout");
    }

    private static PackRegistry CreateRegistry()
        => PackRegistry.ForRepository(TestRepositoryPaths.GetRepoFilePath("."));

    private static string NavigationShellBlueprint()
        => Blueprint("""
            {
              "kind": "wpfui.fluentWindow",
              "properties": { "title": "Composer" },
              "slots": {
                "titleBar": [{ "kind": "wpfui.titleBar", "properties": { "title": "Composer" } }],
                "content": [{
                  "kind": "wpfui.navigationView",
                  "slots": {
                    "items": [{
                      "kind": "wpfui.navigationViewItem",
                      "slots": {
                        "content": [{ "kind": "core.text", "properties": { "text": "Home" } }],
                        "icon": [{ "kind": "wpfui.symbolIcon", "properties": { "symbol": "Home24" } }]
                      }
                    }],
                    "content": [{ "kind": "wpfui.card" }]
                  }
                }]
              }
            }
            """);

    private static string LargeBlueprint(int childCount)
    {
        var children = string.Join(
            ",",
            Enumerable.Range(0, childCount).Select(index =>
                $$"""{ "kind": "core.text", "properties": { "text": "Item {{index}}" } }"""));
        return Blueprint($$"""
            {
              "kind": "wpfui.card",
              "slots": {
                "content": [{
                  "kind": "core.stack",
                  "slots": { "children": [{{children}}] }
                }]
              }
            }
            """);
    }

    private static string Blueprint(string layoutJson)
        => $$"""
            {
              "schemaVersion": "wpfdevtools.ui-blueprint.v1",
              "name": "PerformanceSmoke",
              "packs": [
                { "id": "core", "version": "0.1.0", "required": true, "role": "layout-pack" },
                { "id": "wpfui", "version": "0.1.0", "required": true, "role": "primary" }
              ],
              "primaryPack": "wpfui",
              "layout": {{layoutJson}}
            }
            """;
}
