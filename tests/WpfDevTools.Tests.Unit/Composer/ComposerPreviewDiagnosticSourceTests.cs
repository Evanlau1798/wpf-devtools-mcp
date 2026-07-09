using FluentAssertions;
using System.Reflection;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Mcp.Server.Composer.Preview;
using WpfDevTools.Mcp.Server.Composer.Rendering;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerPreviewDiagnosticSourceTests
{
    private const int GeneratedXamlLineOffset = 2;
    private const int GeneratedXamlColumnOffset = 4;

    [Fact]
    public void PreviewBlueprint_ShouldMapCompileFailureToNestedRendererSource()
    {
        var projectRoot = CreateTempProjectWithBrokenPreviewPack();
        try
        {
            var diagnostics = CreateCompilerPositionDiagnostics(
                projectRoot,
                Blueprint("compilemap.host", "compilemap.badChild"),
                "$.layout.slots.content[0]");

            diagnostics.Should().Contain(diagnostic => diagnostic.Code == "XamlCompileFailed"
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
            var diagnostics = CreateCompilerPositionDiagnostics(
                projectRoot,
                Blueprint("compilemap.brokenHost", "compilemap.goodChild"),
                "$.layout");

            diagnostics.Should().Contain(diagnostic => diagnostic.Code == "XamlCompileFailed"
                && diagnostic.JsonPath == "$.layout"
                && diagnostic.RendererTemplatePath.EndsWith("brokenHost.xaml.sbn", StringComparison.Ordinal));
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void PreviewBlueprint_ShouldNotResolveUndeclaredDottedPackRendererSource()
    {
        var projectRoot = CreateTempProjectWithBrokenPreviewPack();
        try
        {
            var service = new UiBlueprintPreviewService(CreateRegistry(projectRoot));
            var path = ResolveRootRendererTemplatePath(service, """
                {
                  "schemaVersion": "wpfdevtools.ui-blueprint.v1",
                  "name": "UndeclaredOptional",
                  "packs": [{ "id": "wpfui", "version": "0.1.0", "required": true, "role": "primary" }],
                  "primaryPack": "wpfui",
                  "layout": { "kind": "compilemap.brokenHost" }
                }
                """);

            path.Should().BeEmpty();
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

    private static IReadOnlyList<PreviewDiagnostic> CreateCompilerPositionDiagnostics(
        string projectRoot,
        string blueprintJson,
        string jsonPath)
    {
        var render = new UiBlueprintRenderer(CreateRegistry(projectRoot))
            .Render(new RenderBlueprintRequest(blueprintJson));
        render.Valid.Should().BeTrue();
        var source = render.SourceMap.Should()
            .ContainSingle(entry => entry.JsonPath == jsonPath)
            .Subject;
        var buildOutput =
            $"MainWindow.xaml({source.StartLine + GeneratedXamlLineOffset},{source.StartColumn + GeneratedXamlColumnOffset}): error MC3000: simulated compiler error";

        return InvokeCreateDiagnostics(
            buildSucceeded: false,
            buildOutput,
            rendererTemplatePath: "<fallback-renderer.xaml.sbn>",
            render.SourceMap,
            render.Xaml);
    }

    private static IReadOnlyList<PreviewDiagnostic> InvokeCreateDiagnostics(
        bool buildSucceeded,
        string buildOutput,
        string rendererTemplatePath,
        IReadOnlyList<RenderSourceMapEntry> sourceMap,
        string xaml)
        => (IReadOnlyList<PreviewDiagnostic>)typeof(UiBlueprintPreviewService)
            .GetMethod("CreateDiagnostics", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [buildSucceeded, buildOutput, rendererTemplatePath, sourceMap, xaml])!;

    private static string Blueprint(string hostKind, string childKind)
        => $$"""
            {
              "schemaVersion": "wpfdevtools.ui-blueprint.v1",
              "name": "PreviewView",
              "packs": [{ "id": "compilemap", "version": "1.0.0", "required": true, "role": "primary" }],
              "primaryPack": "compilemap",
              "layout": {
                "kind": "{{hostKind}}",
                "slots": { "content": [{ "kind": "{{childKind}}" }] }
              }
            }
            """;

    private static string ResolveRootRendererTemplatePath(UiBlueprintPreviewService service, string blueprintJson)
        => (string)typeof(UiBlueprintPreviewService)
            .GetMethod("ResolveRootRendererTemplatePath", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(service, [blueprintJson])!;

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
