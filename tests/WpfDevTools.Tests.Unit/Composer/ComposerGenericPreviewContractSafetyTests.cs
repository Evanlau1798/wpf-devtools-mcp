using FluentAssertions;
using System.Text.Json.Nodes;
using WpfDevTools.Mcp.Server.Composer.Preview;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed partial class ComposerGenericPreviewContractTests
{
    private const string TrustedRuntimePacksEnvironmentVariable = "WPFDEVTOOLS_COMPOSER_TRUSTED_RUNTIME_PACKS";

    [Fact]
    public void PreviewPackSnapshot_WhenContentChangesAfterDiscovery_ShouldFailClosed()
    {
        var projectRoot = CreateProjectPack(includePreview: true, baseKind: "contentControl");
        try
        {
            var discovered = CreateRegistry(projectRoot).ListPacks().Packs
                .Single(pack => pack.Id == "sample");
            AddRuntimeMetadata(projectRoot, "sample");

            var loaded = UiPreviewPackSnapshot.TryCreate(discovered, out _, out var error);

            loaded.Should().BeFalse();
            error.Should().Contain("changed after discovery");
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public void PreviewContract_WhenRenderedPackFingerprintDiffers_ShouldFailClosed()
    {
        var projectRoot = CreateProjectPack(includePreview: true, baseKind: "contentControl");
        try
        {
            var registry = CreateRegistry(projectRoot);
            var renderedFingerprint = registry.ListPacks().Packs
                .Single(pack => pack.Id == "sample").Fingerprint;
            AddRuntimeMetadata(projectRoot, "sample");

            var result = new UiPackPreviewContractGenerator(registry).Generate(
                Blueprint("sample.panel"),
                "<sample:Panel xmlns:sample=\"urn:sample\" />",
                runtimePackApprovalTokens: null,
                renderedPackFingerprints: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["sample"] = renderedFingerprint
                });

            result.Success.Should().BeFalse();
            result.Diagnostics.Should().ContainSingle(diagnostic =>
                diagnostic.Code == "PreviewPackContentChanged");
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public async Task PreviewBlueprint_ShouldFallbackBeforeRestoringUnapprovedProjectPackage()
    {
        using var trusted = new EnvironmentVariableScope(TrustedRuntimePacksEnvironmentVariable, null);
        var projectRoot = CreateProjectPack(includePreview: true, baseKind: "contentControl");
        var previewRoot = CreateTempDirectory();
        AddRuntimeMetadata(projectRoot, "sample");
        try
        {
            var result = await new UiBlueprintPreviewService(CreateRegistry(projectRoot)).PreviewAsync(
                new PreviewBlueprintRequest(
                    Blueprint("sample.panel"),
                    RestoreEnabled: true,
                    TemporaryRoot: previewRoot,
                    KeepArtifacts: true));

            result.Valid.Should().BeTrue();
            result.VisualFidelity.Should().Be("structural");
            result.Diagnostics.Should().Contain(diagnostic =>
                diagnostic.Code == "PreviewRuntimeDependenciesNotApproved"
                && diagnostic.Message.Contains(TrustedRuntimePacksEnvironmentVariable, StringComparison.Ordinal));
            File.ReadAllText(Path.Combine(previewRoot, "PreviewHost.csproj"))
                .Should().NotContain("Sample.Runtime");
            File.ReadAllText(Path.Combine(previewRoot, "App.xaml"))
                .Should().NotContain("sample:Theme");
            File.Exists(Path.Combine(previewRoot, "PackPreviewStubs.cs")).Should().BeTrue();
        }
        finally
        {
            DeleteDirectory(projectRoot);
            DeleteDirectory(previewRoot);
        }
    }

    [Fact]
    public async Task PreviewBlueprint_WithMatchingCallApproval_ShouldUseRuntimeDependencies()
    {
        using var trusted = new EnvironmentVariableScope(TrustedRuntimePacksEnvironmentVariable, null);
        var projectRoot = CreateProjectPack(includePreview: true, baseKind: "contentControl");
        var previewRoot = CreateTempDirectory();
        AddRuntimeMetadata(projectRoot, "sample");
        var registry = CreateRegistry(projectRoot);
        var token = UiPreviewRuntimeDependencyPolicy.CreateApprovalToken(
            registry.ListPacks().Packs.Single(pack => pack.Id == "sample"));
        try
        {
            var result = await new UiBlueprintPreviewService(registry).PreviewAsync(
                new PreviewBlueprintRequest(
                    Blueprint("sample.panel"),
                    RestoreEnabled: false,
                    TemporaryRoot: previewRoot,
                    KeepArtifacts: true,
                    RuntimePackApprovalTokens: [token]));

            result.Diagnostics.Should().NotContain(diagnostic =>
                diagnostic.Code == "PreviewRuntimeDependenciesNotApproved");
            File.ReadAllText(Path.Combine(previewRoot, "PreviewHost.csproj"))
                .Should().Contain("Sample.Runtime");
            File.ReadAllText(Path.Combine(previewRoot, "App.xaml"))
                .Should().Contain("sample:Theme");
        }
        finally
        {
            DeleteDirectory(projectRoot);
            DeleteDirectory(previewRoot);
        }
    }

    [Fact]
    public async Task PreviewBlueprint_WithMismatchedCallApproval_ShouldRemainStructural()
    {
        using var trusted = new EnvironmentVariableScope(TrustedRuntimePacksEnvironmentVariable, null);
        var projectRoot = CreateProjectPack(includePreview: true, baseKind: "contentControl");
        var previewRoot = CreateTempDirectory();
        AddRuntimeMetadata(projectRoot, "sample");
        try
        {
            var result = await new UiBlueprintPreviewService(CreateRegistry(projectRoot)).PreviewAsync(
                new PreviewBlueprintRequest(
                    Blueprint("sample.panel"),
                    RestoreEnabled: false,
                    TemporaryRoot: previewRoot,
                    KeepArtifacts: true,
                    RuntimePackApprovalTokens: ["sample@1.0.0#" + new string('0', 64)]));

            result.Diagnostics.Should().Contain(diagnostic =>
                diagnostic.Code == "PreviewRuntimeDependenciesNotApproved");
            File.ReadAllText(Path.Combine(previewRoot, "PreviewHost.csproj"))
                .Should().NotContain("Sample.Runtime");
        }
        finally
        {
            DeleteDirectory(projectRoot);
            DeleteDirectory(previewRoot);
        }
    }

    [Fact]
    public async Task PreviewBlueprint_ShouldRejectUnsafeApprovedResourceBeforeWritingProject()
    {
        var projectRoot = CreateProjectPack(includePreview: true, baseKind: "contentControl");
        var previewRoot = Path.Combine(Path.GetTempPath(), "wpfdevtools-preview-safety-" + Guid.NewGuid().ToString("N"));
        AddRuntimeMetadata(projectRoot, "sample", "<ObjectDataProvider />");
        using var trusted = TrustRuntimePacks(projectRoot, "sample");
        try
        {
            var result = await new UiBlueprintPreviewService(CreateRegistry(projectRoot)).PreviewAsync(
                new PreviewBlueprintRequest(
                    Blueprint("sample.panel"),
                    RestoreEnabled: false,
                    TemporaryRoot: previewRoot,
                    KeepArtifacts: true));

            result.Success.Should().BeFalse();
            result.VisualFidelity.Should().Be("not-available");
            result.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == "UnsafePreviewResource");
            var review = result.RuntimePackApprovalReviews.Should().ContainSingle().Which;
            review.ApprovalSource.Should().Be("environment-token");
            review.Approved.Should().BeFalse();
            review.RuntimeEligible.Should().BeFalse();
            review.EligibilityCode.Should().Be("UnsafePreviewResource");
            review.ApprovalToken.Should().BeNull();
            Directory.Exists(previewRoot).Should().BeFalse();
        }
        finally
        {
            DeleteDirectory(projectRoot);
            DeleteDirectory(previewRoot);
        }
    }

    [Theory]
    [InlineData("https://controlled.invalid/theme.xaml")]
    [InlineData("<ResourceDictionary Source=\"https://controlled.invalid/theme.xaml\" />")]
    [InlineData("<ResourceDictionary><ResourceDictionary.Source>file:///C:/private.xaml</ResourceDictionary.Source></ResourceDictionary>")]
    public async Task PreviewBlueprint_ShouldRejectExternalApprovedResourceBeforeWritingProject(string resource)
    {
        var projectRoot = CreateProjectPack(includePreview: true, baseKind: "contentControl");
        var previewRoot = Path.Combine(Path.GetTempPath(), "wpfdevtools-preview-external-resource-" + Guid.NewGuid().ToString("N"));
        AddRuntimeMetadata(projectRoot, "sample", resource);
        using var trusted = TrustRuntimePacks(projectRoot, "sample");
        try
        {
            var result = await new UiBlueprintPreviewService(CreateRegistry(projectRoot)).PreviewAsync(
                new PreviewBlueprintRequest(
                    Blueprint("sample.panel"),
                    RestoreEnabled: false,
                    TemporaryRoot: previewRoot,
                    KeepArtifacts: true));

            result.Success.Should().BeFalse();
            result.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == "UnsafePreviewResource");
            Directory.Exists(previewRoot).Should().BeFalse();
        }
        finally
        {
            DeleteDirectory(projectRoot);
            DeleteDirectory(previewRoot);
        }
    }

    [Fact]
    public async Task PreviewBlueprint_ShouldNormalizeBareApplicationPackResource()
    {
        const string source = "pack://application:,,,/Sample.Runtime;component/Themes/Controls.xaml";
        var projectRoot = CreateProjectPack(includePreview: true, baseKind: "contentControl");
        var previewRoot = CreateTempDirectory();
        AddRuntimeMetadata(projectRoot, "sample", source);
        using var trusted = TrustRuntimePacks(projectRoot, "sample");
        try
        {
            var result = await new UiBlueprintPreviewService(CreateRegistry(projectRoot)).PreviewAsync(
                new PreviewBlueprintRequest(
                    Blueprint("sample.panel"),
                    RestoreEnabled: false,
                    TemporaryRoot: previewRoot,
                    KeepArtifacts: true));

            result.Valid.Should().BeTrue(string.Join(Environment.NewLine, result.Diagnostics.Select(item => item.Message)));
            File.ReadAllText(Path.Combine(previewRoot, "App.xaml")).Should()
                .Contain($"<ResourceDictionary Source=\"{source}\" />");
        }
        finally
        {
            DeleteDirectory(projectRoot);
            DeleteDirectory(previewRoot);
        }
    }

    [Fact]
    public async Task PreviewBlueprint_ShouldAllowApprovedPacksSharingTheSameRuntimeNamespace()
    {
        var projectRoot = CreateProjectPack(includePreview: true, baseKind: "contentControl");
        AddRuntimeMetadata(projectRoot, "sample");
        AddSharedNamespaceProjectPack(projectRoot);
        RemoveDuplicateSharedPreviewTypes(projectRoot);
        using var trusted = TrustRuntimePacks(projectRoot, "sample", "other");
        try
        {
            var result = await new UiBlueprintPreviewService(CreateRegistry(projectRoot)).PreviewAsync(
                new PreviewBlueprintRequest(SharedNamespaceBlueprint(), RestoreEnabled: false));

            result.Valid.Should().BeTrue();
            result.Diagnostics.Should().NotContain(diagnostic =>
                diagnostic.Code == "PreviewNamespacePrefixConflict"
                || diagnostic.Code == "PackXmlNamespaceConflict");
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public async Task PreviewBlueprint_ShouldReportSharedNamespaceWhenRuntimeAndStubMappingsCannotCoexist()
    {
        var projectRoot = CreateProjectPack(includePreview: true, baseKind: "contentControl");
        AddRuntimeMetadata(projectRoot, "sample");
        AddSharedNamespaceProjectPack(projectRoot);
        using var trusted = TrustRuntimePacks(projectRoot, "sample");
        try
        {
            var result = await new UiBlueprintPreviewService(CreateRegistry(projectRoot)).PreviewAsync(
                new PreviewBlueprintRequest(SharedNamespaceBlueprint(), RestoreEnabled: false));

            result.Success.Should().BeFalse();
            result.VisualFidelity.Should().Be("not-available");
            result.Diagnostics.Should().ContainSingle(diagnostic =>
                diagnostic.Code == "PreviewNamespacePrefixConflict");
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    private static void AddRuntimeMetadata(
        string projectRoot,
        string packId,
        string resource = "<sample:Theme />")
    {
        var packPath = Path.Combine(projectRoot, ".wpfdevtools", "packs", packId, "1.0.0", "pack.json");
        var pack = JsonNode.Parse(File.ReadAllText(packPath))!.AsObject();
        pack["nugetPackages"] = new JsonArray
        {
            new JsonObject
            {
                ["id"] = packId == "sample" ? "Sample.Runtime" : "Other.Runtime",
                ["versionRange"] = "[1.0.0]",
                ["contentHash"] = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=="
            }
        };
        var resources = new JsonArray();
        resources.Add(resource);
        pack["resourceSetup"] = new JsonObject { ["applicationMergedDictionaries"] = resources };
        File.WriteAllText(packPath, pack.ToJsonString());
    }

    private static void AddSharedNamespaceProjectPack(string projectRoot)
    {
        var source = Path.Combine(projectRoot, ".wpfdevtools", "packs", "sample", "1.0.0");
        var destination = Path.Combine(projectRoot, ".wpfdevtools", "packs", "other", "1.0.0");
        foreach (var sourceFile in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var destinationFile = Path.Combine(destination, Path.GetRelativePath(source, sourceFile));
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
            var content = File.ReadAllText(sourceFile)
                .Replace("sample.panel", "other.panel", StringComparison.Ordinal)
                .Replace("\"id\":\"sample\"", "\"id\":\"other\"", StringComparison.Ordinal)
                .Replace("Sample.Runtime", "Other.Runtime", StringComparison.Ordinal);
            File.WriteAllText(destinationFile, content);
        }
    }

    private static EnvironmentVariableScope TrustRuntimePacks(string projectRoot, params string[] packIds)
    {
        var packs = CreateRegistry(projectRoot).ListPacks().Packs;
        var tokens = packIds.Select(packId => UiPreviewRuntimeDependencyPolicy.CreateApprovalToken(
            packs.Single(pack => pack.Id == packId)));
        return new EnvironmentVariableScope(
            TrustedRuntimePacksEnvironmentVariable,
            string.Join(';', tokens));
    }

    private static void RemoveDuplicateSharedPreviewTypes(string projectRoot)
    {
        var packPath = Path.Combine(projectRoot, ".wpfdevtools", "packs", "other", "1.0.0", "pack.json");
        var pack = JsonNode.Parse(File.ReadAllText(packPath))!.AsObject();
        var types = pack["preview"]!["types"]!.AsObject();
        types.Remove("Label");
        types["OtherPanel"] = types["Panel"]!.DeepClone();
        types.Remove("Panel");
        File.WriteAllText(packPath, pack.ToJsonString());
        var rendererPath = Path.Combine(
            Path.GetDirectoryName(packPath)!,
            "renderers",
            "xaml",
            "panel.xaml.sbn");
        File.WriteAllText(rendererPath, "<sample:OtherPanel Caption=\"{{caption}}\" />");
    }

    private static string SharedNamespaceBlueprint()
        => """
            {
              "schemaVersion": "wpfdevtools.ui-blueprint.v1",
              "name": "SharedNamespace",
              "packs": [
                { "id": "core", "version": "0.1.0", "required": true, "role": "layout-pack" },
                { "id": "sample", "version": "1.0.0", "required": true, "role": "primary" },
                { "id": "other", "version": "1.0.0", "required": true, "role": "extension" }
              ],
              "primaryPack": "sample",
              "layout": {
                "kind": "core.stack",
                "slots": { "children": [
                  { "kind": "sample.panel" },
                  { "kind": "other.panel" }
                ] }
              }
            }
            """;

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _original;

        public EnvironmentVariableScope(string name, string? value)
        {
            _name = name;
            _original = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
            => Environment.SetEnvironmentVariable(_name, _original);
    }
}
