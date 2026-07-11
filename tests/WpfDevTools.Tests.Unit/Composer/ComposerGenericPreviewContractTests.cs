using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Mcp.Server.Composer.Preview;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerGenericPreviewContractTests
{
    [Fact]
    public async Task PreviewBlueprint_ShouldCompileMetadataDefinedThirdPartyPack()
    {
        var projectRoot = CreateProjectPack(includePreview: true, baseKind: "contentControl");
        var previewRoot = CreateTempDirectory();
        try
        {
            var result = await new UiBlueprintPreviewService(CreateRegistry(projectRoot)).PreviewAsync(
                new PreviewBlueprintRequest(
                    Blueprint("sample.panel"),
                    RestoreEnabled: true,
                    StartHost: false,
                    TemporaryRoot: previewRoot,
                    KeepArtifacts: true));

            result.BuildSucceeded.Should().BeTrue(result.BuildOutput);
            var source = File.ReadAllText(Path.Combine(previewRoot, "PackPreviewStubs.cs"));
            source.Should().Contain("namespace Sample.Controls")
                .And.Contain("class Panel : ContentControl")
                .And.NotContain("Wpf.Ui");
        }
        finally
        {
            DeleteDirectory(projectRoot);
            DeleteDirectory(previewRoot);
        }
    }

    [Fact]
    public async Task PreviewBlueprint_ShouldReportMissingPreviewContract()
    {
        var projectRoot = CreateProjectPack(includePreview: false, baseKind: "contentControl");
        try
        {
            var result = await new UiBlueprintPreviewService(CreateRegistry(projectRoot)).PreviewAsync(
                new PreviewBlueprintRequest(Blueprint("sample.panel"), RestoreEnabled: false));

            result.Success.Should().BeFalse();
            result.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Code == "PreviewContractMissing");
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public async Task PreviewBlueprint_ShouldRejectUnsafePreviewBaseKind()
    {
        var projectRoot = CreateProjectPack(includePreview: true, baseKind: "arbitraryCode");
        try
        {
            var result = await new UiBlueprintPreviewService(CreateRegistry(projectRoot)).PreviewAsync(
                new PreviewBlueprintRequest(Blueprint("sample.panel"), RestoreEnabled: false));

            result.Success.Should().BeFalse();
            result.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Code == "PreviewContractInvalid");
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public async Task PreviewBlueprint_ShouldCompileNewWpfUiControlsFromPackMetadata()
    {
        var blueprint = """
            {
              "schemaVersion": "wpfdevtools.ui-blueprint.v1",
              "name": "WpfUiPreviewContract",
              "packs": [
                { "id": "core", "version": "0.1.0", "required": true, "role": "layout-pack" },
                { "id": "wpfui", "version": "0.1.0", "required": true, "role": "primary" }
              ],
              "primaryPack": "wpfui",
              "layout": {
                "kind": "core.stack",
                "slots": { "children": [
                  { "kind": "wpfui.numberBox" },
                  { "kind": "wpfui.toggleSwitch" },
                  { "kind": "wpfui.progressRing" }
                ] }
              }
            }
            """;

        var result = await new UiBlueprintPreviewService(CreateRegistry()).PreviewAsync(
            new PreviewBlueprintRequest(blueprint, RestoreEnabled: true));

        result.BuildSucceeded.Should().BeTrue(result.BuildOutput);
    }

    private static PackRegistry CreateRegistry(string? projectRoot = null)
    {
        var repoRoot = TestRepositoryPaths.GetRepoFilePath(".");
        return new PackRegistry(
            ComposerPackPaths.BuiltinRoot(repoRoot),
            projectRoot is null ? null : ComposerPackPaths.ProjectLocalRoot(projectRoot));
    }

    private static string CreateProjectPack(bool includePreview, string baseKind)
    {
        var projectRoot = CreateTempDirectory();
        var packRoot = Path.Combine(projectRoot, ".wpfdevtools", "packs", "sample", "1.0.0");
        Directory.CreateDirectory(Path.Combine(packRoot, "blocks"));
        Directory.CreateDirectory(Path.Combine(packRoot, "renderers", "xaml"));
        File.WriteAllText(Path.Combine(packRoot, "install.manifest.json"),
            """{"schemaVersion":"wpfdevtools.pack-install-manifest.v1","id":"sample","version":"1.0.0","scope":"project","path":".","enabled":true}""");
        var preview = includePreview
            ? """
              ,"preview":{
                "namespaceUri":"urn:sample-controls",
                "clrNamespace":"Sample.Controls",
                "types":{
                  "Panel":{"baseKind":"BASE_KIND","contentProperty":"Caption","properties":{"Caption":"string"}},
                  "Label":{"baseKind":"contentControl","contentProperty":"Text","properties":{"Text":"string"}}
                }
              }
              """
                .Replace("BASE_KIND", baseKind, StringComparison.Ordinal)
            : string.Empty;
        File.WriteAllText(Path.Combine(packRoot, "pack.json"),
            """{"schemaVersion":"wpfdevtools.ui-pack.v1","id":"sample","displayName":"Sample","version":"1.0.0","blocks":["sample.panel"],"recipes":[],"xmlNamespaces":{"sample":"urn:sample-controls"}PREVIEW}"""
                .Replace("PREVIEW", preview, StringComparison.Ordinal));
        File.WriteAllText(Path.Combine(packRoot, "source.lock.json"),
            """{"schemaVersion":"wpfdevtools.source-lock.v1","sources":[{"name":"Sample","url":"https://example.invalid/sample","version":"1.0.0","paths":["src"]}],"transformPolicy":{}}""");
        File.WriteAllText(Path.Combine(packRoot, "blocks", "panel.block.json"),
            """{"schemaVersion":"wpfdevtools.ui-block.v1","kind":"sample.panel","displayName":"Panel","category":"container","properties":{"caption":{"type":"string","default":"Preview"}},"slots":{},"renderer":{"xamlTemplate":"renderers/xaml/panel.xaml.sbn"},"sourceHints":[]}""");
        File.WriteAllText(Path.Combine(packRoot, "renderers", "xaml", "panel.xaml.sbn"),
            "<sample:Panel Caption=\"{{caption}}\"><sample:Label Text=\"Built from metadata\" /></sample:Panel>");
        return projectRoot;
    }

    private static string Blueprint(string kind)
        => $$"""
            {
              "schemaVersion": "wpfdevtools.ui-blueprint.v1",
              "name": "ThirdPartyPreview",
              "packs": [{ "id": "sample", "version": "1.0.0", "required": true, "role": "primary" }],
              "primaryPack": "sample",
              "layout": { "kind": "{{kind}}" }
            }
            """;

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "wpfdevtools-preview-contract-" + Guid.NewGuid().ToString("N"));
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
