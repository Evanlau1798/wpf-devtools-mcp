using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Mcp.Server.Composer.Preview;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerPreviewDiagnosticSourceTests
{
    [Fact]
    public void PreviewBlueprint_ShouldMapCompileFailureToNestedRendererSource()
    {
        var projectRoot = CreateTempProjectWithBrokenPreviewPack();
        try
        {
            var service = new UiBlueprintPreviewService(CreateRegistry(projectRoot));

            var result = service.Preview(new PreviewBlueprintRequest(Blueprint("compilemap.host", "compilemap.badChild"), RestoreEnabled: true));

            result.BuildSucceeded.Should().BeFalse(result.BuildOutput);
            result.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == "XamlCompileFailed"
                && diagnostic.JsonPath == "$.layout.slots.content[0]"
                && diagnostic.RendererTemplatePath.EndsWith("badChild.xaml.sbn", StringComparison.Ordinal));
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void PreviewBlueprint_ShouldMapCompileFailureToRootRendererSource()
    {
        var projectRoot = CreateTempProjectWithBrokenPreviewPack();
        try
        {
            var service = new UiBlueprintPreviewService(CreateRegistry(projectRoot));

            var result = service.Preview(new PreviewBlueprintRequest(Blueprint("compilemap.brokenHost", "compilemap.goodChild"), RestoreEnabled: true));

            result.BuildSucceeded.Should().BeFalse(result.BuildOutput);
            result.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == "XamlCompileFailed"
                && diagnostic.JsonPath == "$.layout"
                && diagnostic.RendererTemplatePath.EndsWith("brokenHost.xaml.sbn", StringComparison.Ordinal));
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    private static PackRegistry CreateRegistry(string projectRoot)
        => new(
            ComposerPackPaths.BuiltinRoot(TestRepositoryPaths.GetRepoFilePath(".")),
            ComposerPackPaths.ProjectLocalRoot(projectRoot),
            null);

    private static string Blueprint(string hostKind, string childKind)
        => $$"""
            {
              "schemaVersion": "wpfdevtools.ui-blueprint.v1",
              "name": "PreviewView",
              "packs": [{ "id": "compilemap", "version": "1.0.0", "required": true }],
              "primaryPack": "compilemap",
              "layout": {
                "kind": "{{hostKind}}",
                "slots": { "content": [{ "kind": "{{childKind}}" }] }
              }
            }
            """;

    private static string CreateTempProjectWithBrokenPreviewPack()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), "wpfdevtools-preview-map-" + Guid.NewGuid().ToString("N"));
        var packRoot = Path.Combine(projectRoot, ".wpfdevtools", "packs", "compilemap", "1.0.0");
        Directory.CreateDirectory(Path.Combine(packRoot, "blocks"));
        Directory.CreateDirectory(Path.Combine(packRoot, "recipes"));
        Directory.CreateDirectory(Path.Combine(packRoot, "examples"));
        Directory.CreateDirectory(Path.Combine(packRoot, "renderers", "xaml"));

        File.WriteAllText(Path.Combine(packRoot, "install.manifest.json"), """
            {"schemaVersion":"wpfdevtools.pack-install-manifest.v1","id":"compilemap","version":"1.0.0","scope":"project-local","path":".","enabled":true}
            """);
        File.WriteAllText(Path.Combine(packRoot, "pack.json"), """
            {"schemaVersion":"wpfdevtools.ui-pack.v1","id":"compilemap","displayName":"Compile Map","version":"1.0.0","blocks":["compilemap.host","compilemap.badChild","compilemap.brokenHost","compilemap.goodChild"],"recipes":[]}
            """);
        File.WriteAllText(Path.Combine(packRoot, "source.lock.json"), """
            {"schemaVersion":"wpfdevtools.source-lock.v1","sources":[],"transformPolicy":{}}
            """);
        File.WriteAllText(Path.Combine(packRoot, "blocks", "host.block.json"), """
            {"schemaVersion":"wpfdevtools.ui-block.v1","kind":"compilemap.host","displayName":"Host","category":"test","properties":{},"slots":{"content":{"allowedKinds":["compilemap.badChild"]}},"renderer":{"xamlTemplate":"renderers/xaml/host.xaml.sbn"},"sourceHints":[]}
            """);
        File.WriteAllText(Path.Combine(packRoot, "blocks", "badChild.block.json"), """
            {"schemaVersion":"wpfdevtools.ui-block.v1","kind":"compilemap.badChild","displayName":"Bad Child","category":"test","properties":{},"slots":{},"renderer":{"xamlTemplate":"renderers/xaml/badChild.xaml.sbn"},"sourceHints":[]}
            """);
        File.WriteAllText(Path.Combine(packRoot, "blocks", "brokenHost.block.json"), """
            {"schemaVersion":"wpfdevtools.ui-block.v1","kind":"compilemap.brokenHost","displayName":"Broken Host","category":"test","properties":{},"slots":{"content":{"allowedKinds":["compilemap.goodChild"]}},"renderer":{"xamlTemplate":"renderers/xaml/brokenHost.xaml.sbn"},"sourceHints":[]}
            """);
        File.WriteAllText(Path.Combine(packRoot, "blocks", "goodChild.block.json"), """
            {"schemaVersion":"wpfdevtools.ui-block.v1","kind":"compilemap.goodChild","displayName":"Good Child","category":"test","properties":{},"slots":{},"renderer":{"xamlTemplate":"renderers/xaml/goodChild.xaml.sbn"},"sourceHints":[]}
            """);
        File.WriteAllText(Path.Combine(packRoot, "renderers", "xaml", "host.xaml.sbn"), "<Grid>{{ slot.content }}</Grid>");
        File.WriteAllText(Path.Combine(packRoot, "renderers", "xaml", "badChild.xaml.sbn"), "<TextBlock></NotTextBlock>");
        File.WriteAllText(Path.Combine(packRoot, "renderers", "xaml", "brokenHost.xaml.sbn"), "<Grid>{{ slot.content }}</BrokenGrid>");
        File.WriteAllText(Path.Combine(packRoot, "renderers", "xaml", "goodChild.xaml.sbn"), "<TextBlock />");
        return projectRoot;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
