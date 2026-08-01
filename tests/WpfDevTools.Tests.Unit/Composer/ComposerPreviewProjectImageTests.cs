using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Mcp.Server.Composer.Preview;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerPreviewProjectImageTests
{
    [Fact]
    public async Task PreviewBlueprint_ShouldCopyApplicationLocalImagesAsWpfResources()
    {
        var projectRoot = TestDirectory.Create();
        var previewRoot = TestDirectory.Create();
        try
        {
            File.WriteAllText(Path.Combine(projectRoot, "PreviewApp.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><UseWPF>true</UseWPF></PropertyGroup></Project>");
            Directory.CreateDirectory(Path.Combine(projectRoot, "Assets"));
            File.WriteAllBytes(Path.Combine(projectRoot, "Assets", "hero.png"), [1, 2, 3]);

            var result = await new UiBlueprintPreviewService(PackRegistry.ForRepository(
                    TestRepositoryPaths.GetRepoFilePath(".")))
                .PreviewAsync(new PreviewBlueprintRequest(
                    Blueprint,
                    RestoreEnabled: false,
                    KeepArtifacts: true,
                    TemporaryRoot: previewRoot,
                    ProjectRoot: projectRoot));

            result.Valid.Should().BeTrue(result.Diagnostics.FirstOrDefault()?.Message);
            File.ReadAllBytes(Path.Combine(previewRoot, "Assets", "hero.png")).Should().Equal(1, 2, 3);
            File.ReadAllText(Path.Combine(previewRoot, "PreviewHost.csproj"))
                .Should().Contain("<Resource Include=\"Assets\\hero.png\" />");
        }
        finally
        {
            TestDirectory.Delete(projectRoot);
            TestDirectory.Delete(previewRoot);
        }
    }

    private const string Blueprint = """
        {
          "schemaVersion":"wpfdevtools.ui-blueprint.v1",
          "name":"ImagePreview",
          "packs":[{"id":"core","version":"0.1.0","required":true,"role":"primary"}],
          "primaryPack":"core",
          "layout":{"kind":"core.image","properties":{"source":"/Assets/hero.png","automationName":"Hero"}}
        }
        """;
}
