using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Mcp.Server.Composer.Rendering;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerRenderedNameCollisionTests
{
    [Fact]
    public void Renderer_ShouldRejectRepeatedThirdPartyNamesInOneNamescope()
    {
        var projectRoot = CreatePack("<Grid x:Name=\"SharedPart\" />");
        try
        {
            var result = Render(projectRoot);

            result.Success.Should().BeFalse();
            result.Errors.Should().ContainSingle(issue =>
                issue.Code == "RenderedNameCollision"
                && issue.JsonPath == "$.layout.slots.children[1]"
                && issue.Message.Contains("$.layout.slots.children[0]", StringComparison.Ordinal));
            result.SourceMap.Where(entry => entry.BlockKind == "sample.namedPart")
                .Select(entry => entry.StartIndex).Should().OnlyHaveUniqueItems();
        }
        finally
        {
            TestDirectory.Delete(projectRoot);
        }
    }

    [Fact]
    public void Renderer_ShouldAllowRepeatedNamesInSeparateTemplateNamescopes()
    {
        var projectRoot = CreatePack(
            "<ContentControl><ContentControl.ContentTemplate><DataTemplate><Grid x:Name=\"SharedPart\" /></DataTemplate></ContentControl.ContentTemplate></ContentControl>");
        try
        {
            var result = Render(projectRoot);

            result.Success.Should().BeTrue(result.Errors.FirstOrDefault()?.Message);
            result.Errors.Should().NotContain(issue => issue.Code == "RenderedNameCollision");
        }
        finally
        {
            TestDirectory.Delete(projectRoot);
        }
    }

    private static RenderBlueprintResult Render(string projectRoot)
    {
        var registry = new PackRegistry(
            ComposerPackPaths.BuiltinRoot(TestRepositoryPaths.GetRepoFilePath(".")),
            ComposerPackPaths.ProjectLocalRoot(projectRoot));
        return new UiBlueprintRenderer(registry).Render(new RenderBlueprintRequest(Blueprint()));
    }

    private static string CreatePack(string childRenderer)
    {
        var projectRoot = TestDirectory.Create();
        var packRoot = Path.Combine(projectRoot, ".wpfdevtools", "packs", "sample", "1.0.0");
        Directory.CreateDirectory(Path.Combine(packRoot, "blocks"));
        Directory.CreateDirectory(Path.Combine(packRoot, "renderers", "xaml"));
        File.WriteAllText(Path.Combine(packRoot, "pack.json"),
            """{"schemaVersion":"wpfdevtools.ui-pack.v1","id":"sample","kind":"control-pack","displayName":"Sample","version":"1.0.0","blocks":["sample.host","sample.namedPart"],"recipes":[]}""");
        File.WriteAllText(Path.Combine(packRoot, "source.lock.json"),
            """{"schemaVersion":"wpfdevtools.source-lock.v1","sources":[],"transformPolicy":{}}""");
        File.WriteAllText(Path.Combine(packRoot, "install.manifest.json"),
            """{"schemaVersion":"wpfdevtools.pack-install-manifest.v1","id":"sample","version":"1.0.0","scope":"project-local","path":".","enabled":true}""");
        File.WriteAllText(Path.Combine(packRoot, "blocks", "host.block.json"),
            """{"schemaVersion":"wpfdevtools.ui-block.v1","kind":"sample.host","displayName":"Host","description":"Host.","category":"container","properties":{},"slots":{"children":{"allowedKinds":["sample.namedPart"]}},"renderer":{"xamlTemplate":"renderers/xaml/host.xaml.sbn"},"sourceHints":[]}""");
        File.WriteAllText(Path.Combine(packRoot, "blocks", "named-part.block.json"),
            """{"schemaVersion":"wpfdevtools.ui-block.v1","kind":"sample.namedPart","displayName":"Named part","description":"Named part.","category":"display","properties":{},"slots":{},"renderer":{"xamlTemplate":"renderers/xaml/named-part.xaml.sbn"},"sourceHints":[]}""");
        File.WriteAllText(Path.Combine(packRoot, "renderers", "xaml", "host.xaml.sbn"),
            "<StackPanel>{{slot.children}}</StackPanel>");
        File.WriteAllText(Path.Combine(packRoot, "renderers", "xaml", "named-part.xaml.sbn"), childRenderer);
        return projectRoot;
    }

    private static string Blueprint()
        => """
           {"schemaVersion":"wpfdevtools.ui-blueprint.v1","name":"RepeatedNames","packs":[{"id":"sample","version":"1.0.0","required":true,"role":"primary"}],"primaryPack":"sample","layout":{"kind":"sample.host","slots":{"children":[{"kind":"sample.namedPart"},{"kind":"sample.namedPart"}]}}}
           """;
}
