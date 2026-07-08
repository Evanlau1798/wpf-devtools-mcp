using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Apply;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Mcp.Server.Composer.Rendering;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerRendererNamespaceTests
{
    [Fact]
    public void RenderBlueprint_ShouldDeclareRootNamespacesFromPackManifest()
    {
        var renderer = new UiBlueprintRenderer(CreateRegistry());

        var result = renderer.Render(new RenderBlueprintRequest(FluentWindowBlueprint()));

        result.Success.Should().BeTrue();
        result.Xaml.Should().StartWith("<ui:FluentWindow ");
        result.Xaml.Should().Contain("xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"");
        result.Xaml.Should().Contain("xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"");
        result.Xaml.Should().Contain("xmlns:ui=\"http://schemas.lepo.co/wpfui/2022/xaml\"");
    }

    [Fact]
    public void ApplyBlueprint_ShouldWritePackNamespacesInGeneratedViewXaml()
    {
        var service = new UiBlueprintApplyService(CreateRegistry());
        var projectRoot = Path.Combine(Path.GetTempPath(), "wpfdevtools-composer-namespace-" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = service.Apply(new ApplyBlueprintRequest(FluentWindowBlueprint(), projectRoot));

            result.Success.Should().BeTrue();
            result.Xaml.Should().Contain("xmlns:ui=\"http://schemas.lepo.co/wpfui/2022/xaml\"");
        }
        finally
        {
            if (Directory.Exists(projectRoot))
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }
    }

    private static PackRegistry CreateRegistry()
        => PackRegistry.ForRepository(TestRepositoryPaths.GetRepoFilePath("."));

    private static string FluentWindowBlueprint()
        => """
            {
              "schemaVersion": "wpfdevtools.ui-blueprint.v1",
              "name": "GeneratedView",
              "packs": [{ "id": "wpfui", "version": "0.1.0", "required": true, "role": "primary" }],
              "primaryPack": "wpfui",
              "layout": {
                "kind": "wpfui.fluentWindow",
                "properties": { "title": "Composer Generated App" },
                "slots": {
                  "titleBar": [{ "kind": "wpfui.titleBar", "properties": { "title": "Composer Generated App" } }],
                  "content": [{ "kind": "wpfui.navigationView" }]
                }
              }
            }
            """;
}
