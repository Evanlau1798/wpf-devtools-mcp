using System.Diagnostics;
using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer;
using WpfDevTools.Mcp.Server.Composer.Blueprints;
using WpfDevTools.Mcp.Server.Composer.Catalog;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Mcp.Server.Composer.Preview;
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
    public void PackRegistryLoad_ShouldStayWithinSmokeTarget()
    {
        var result = Measure(() => CreateRegistry().ListPacks(), out var elapsed);

        result.Packs.Should().Contain(pack => pack.Id == "wpfui" && pack.Version == "0.1.0");
        AssertWithin(elapsed, ComposerPerformanceTargets.PackRegistryLoad, "pack registry load");
    }

    [Fact]
    public void BlockCatalogQuery_ShouldStayWithinSmokeTarget()
    {
        var catalog = new BlockCatalogService(CreateRegistry());

        var result = Measure(
            () => catalog.GetCatalog(new BlockCatalogQuery(Kind: "wpfui.button")),
            out var elapsed);

        result.Items.Should().ContainSingle(item => item.Kind == "wpfui.button");
        AssertWithin(elapsed, ComposerPerformanceTargets.BlockCatalogQuery, "block catalog query");
    }

    [Fact]
    public void BlueprintValidation_ShouldStayWithinSmokeTarget()
    {
        var validator = new BlueprintValidationService(CreateRegistry());

        var result = Measure(() => validator.Validate(NavigationShellBlueprint()), out var elapsed);

        result.Success.Should().BeTrue();
        AssertWithin(elapsed, ComposerPerformanceTargets.BlueprintValidation, "blueprint validation");
    }

    [Fact]
    public void RendererDryRun_ShouldStayWithinSmokeTarget()
    {
        var renderer = new UiBlueprintRenderer(CreateRegistry());

        var result = Measure(
            () => renderer.Render(new RenderBlueprintRequest(NavigationShellBlueprint())),
            out var elapsed);

        result.Success.Should().BeTrue();
        result.DryRun.Should().BeTrue();
        AssertWithin(elapsed, ComposerPerformanceTargets.RendererDryRun, "renderer dry-run");
    }

    [Fact]
    public void PreviewSmoke_ShouldStayWithinSmokeTarget()
    {
        var service = new UiBlueprintPreviewService(CreateRegistry());

        var result = Measure(
            () => service.Preview(new PreviewBlueprintRequest(ButtonBlueprint(), RestoreEnabled: false)),
            out var elapsed);

        result.Success.Should().BeTrue();
        result.RestoreEnabled.Should().BeFalse();
        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == "XamlCompileFailed");
        AssertWithin(elapsed, ComposerPerformanceTargets.PreviewSmoke, "preview smoke");
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

    private static T Measure<T>(Func<T> action, out TimeSpan elapsed)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = action();
        stopwatch.Stop();
        elapsed = stopwatch.Elapsed;
        return result;
    }

    private static void AssertWithin(TimeSpan elapsed, TimeSpan target, string operation)
        => elapsed.Should().BeLessThan(target, $"{operation} should stay within the Phase 8 smoke target");

    private static PackRegistry CreateRegistry()
        => PackRegistry.ForRepository(TestRepositoryPaths.GetRepoFilePath("."));

    private static string ButtonBlueprint()
        => Blueprint("""{ "kind": "wpfui.button", "properties": { "text": "Save" } }""");

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
                        "content": [{ "kind": "text", "properties": { "value": "Home" } }],
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
                $$"""{ "kind": "text", "properties": { "value": "Item {{index}}" } }"""));
        return Blueprint($$"""
            {
              "kind": "wpfui.card",
              "slots": {
                "content": [{
                  "kind": "stack",
                  "slots": { "stack": [{{children}}] }
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
              "packs": [{ "id": "wpfui", "version": "0.1.0", "required": true, "role": "primary" }],
              "primaryPack": "wpfui",
              "layout": {{layoutJson}}
            }
            """;
}
