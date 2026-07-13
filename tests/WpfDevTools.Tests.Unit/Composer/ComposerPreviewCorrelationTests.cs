using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Mcp.Server.Composer.Preview;
using WpfDevTools.Mcp.Server.Composer.Rendering;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerPreviewCorrelationTests
{
    [Fact]
    public void Renderer_ShouldAddTransientNamesOnlyWhenPreviewCorrelationIsRequested()
    {
        var renderer = new UiBlueprintRenderer(CreateRegistry());

        var normal = renderer.Render(new RenderBlueprintRequest(Blueprint()));
        var preview = renderer.Render(new RenderBlueprintRequest(
            Blueprint(),
            IncludeTransientElementCorrelation: true));

        normal.Success.Should().BeTrue(normal.Errors.FirstOrDefault()?.Message);
        normal.Xaml.Should().NotContain("WpfDevToolsBp_");
        normal.ElementCorrelations.Should().BeEmpty();
        preview.Success.Should().BeTrue(preview.Errors.FirstOrDefault()?.Message);
        preview.ElementCorrelations.Should().HaveCount(2);
        preview.ElementCorrelations.Select(item => item.JsonPath).Should().BeEquivalentTo(
            "$.layout",
            "$.layout.slots.children[0]");
        preview.ElementCorrelations.Select(item => item.ElementName).Should().OnlyHaveUniqueItems();
        preview.Xaml.Should().Contain("x:Name=\"WpfDevToolsBp_");
    }

    [Fact]
    public async Task Preview_ShouldReturnTransientRendererRootCorrelationWithoutChangingBlueprint()
    {
        var result = await new UiBlueprintPreviewService(CreateRegistry()).PreviewAsync(
            new PreviewBlueprintRequest(Blueprint(), RestoreEnabled: false),
            CancellationToken.None);

        result.Valid.Should().BeTrue(result.Diagnostics.FirstOrDefault()?.Message);
        result.ElementCorrelations.Should().HaveCount(2);
        result.ElementCorrelations.Should().OnlyContain(item =>
            item.ElementName.StartsWith("WpfDevToolsBp_", StringComparison.Ordinal));
        Blueprint().Should().NotContain("WpfDevToolsBp_");
    }

    private static PackRegistry CreateRegistry()
        => PackRegistry.ForRepository(TestRepositoryPaths.GetRepoFilePath("."));

    private static string Blueprint()
        => """
           {
             "schemaVersion": "wpfdevtools.ui-blueprint.v1",
             "name": "PreviewCorrelation",
             "packs": [
               { "id": "core", "version": "0.1.0", "required": true, "role": "primary" }
             ],
             "primaryPack": "core",
             "layout": {
               "kind": "core.stack",
               "slots": {
                 "children": [
                   { "kind": "core.text", "properties": { "text": "Correlated" } }
                 ]
               }
             }
           }
           """;
}
