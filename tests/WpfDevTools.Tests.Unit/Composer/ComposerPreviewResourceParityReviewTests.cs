using FluentAssertions;
using System.Text.Json.Nodes;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Mcp.Server.Composer.Preview;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed partial class ComposerGenericPreviewContractTests
{
    [Fact]
    public async Task PreviewBlueprint_ShouldNotReuseRuntimeApprovalAcrossProjectRoots()
    {
        var approvedRoot = CreateProjectPack(includePreview: true, baseKind: "contentControl");
        var otherRoot = CreateProjectPack(includePreview: true, baseKind: "contentControl");
        AddRuntimeMetadata(approvedRoot, "sample");
        AddRuntimeMetadata(otherRoot, "sample");
        using var trusted = TrustRuntimePacks(approvedRoot, "sample");
        try
        {
            var result = await new UiBlueprintPreviewService(CreateRegistry(otherRoot)).PreviewAsync(
                new PreviewBlueprintRequest(Blueprint("sample.panel"), RestoreEnabled: false));

            result.UsesStructuralStubs.Should().BeTrue();
            result.UsesRuntimeDependencies.Should().BeFalse();
            result.Diagnostics.Should().Contain(diagnostic =>
                diagnostic.Code == "PreviewRuntimeDependenciesNotApproved");
        }
        finally
        {
            DeleteDirectory(approvedRoot);
            DeleteDirectory(otherRoot);
        }
    }

    [Fact]
    public async Task PreviewBlueprint_ShouldInvalidateRuntimeApprovalAfterPackMutation()
    {
        var projectRoot = CreateProjectPack(includePreview: true, baseKind: "contentControl");
        AddRuntimeMetadata(projectRoot, "sample");
        using var trusted = TrustRuntimePacks(projectRoot, "sample");
        try
        {
            var packPath = Path.Combine(projectRoot, ".wpfdevtools", "packs", "sample", "1.0.0", "pack.json");
            File.AppendAllText(packPath, Environment.NewLine);

            var result = await new UiBlueprintPreviewService(CreateRegistry(projectRoot)).PreviewAsync(
                new PreviewBlueprintRequest(Blueprint("sample.panel"), RestoreEnabled: false));

            result.UsesStructuralStubs.Should().BeTrue();
            result.UsesRuntimeDependencies.Should().BeFalse();
            result.Diagnostics.Should().Contain(diagnostic =>
                diagnostic.Code == "PreviewRuntimeDependenciesNotApproved");
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public async Task PreviewBlueprint_ShouldKeepUnpinnedRuntimePackagesStructural()
    {
        var projectRoot = CreateProjectPack(includePreview: true, baseKind: "contentControl");
        AddRuntimeMetadata(projectRoot, "sample");
        SetRuntimePackage(projectRoot, "sample", "1.0.0", contentHash: null);
        using var trusted = TrustRuntimePacks(projectRoot, "sample");
        try
        {
            var result = await new UiBlueprintPreviewService(CreateRegistry(projectRoot)).PreviewAsync(
                new PreviewBlueprintRequest(Blueprint("sample.panel"), RestoreEnabled: false));

            result.UsesStructuralStubs.Should().BeTrue();
            result.UsesRuntimeDependencies.Should().BeFalse();
            result.Diagnostics.Should().Contain(diagnostic =>
                diagnostic.Code == "PreviewRuntimePackageNotImmutable");
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    [Theory]
    [InlineData("<ResourceDictionary Source=\"pack://application:,,,/Sample.Runtime;component/Themes/Controls.xaml\" />")]
    [InlineData("<ResourceDictionary><ResourceDictionary.Source>pack://application:,,,/Sample.Runtime;component/Themes/Controls.xaml</ResourceDictionary.Source></ResourceDictionary>")]
    public async Task PreviewBlueprint_ShouldAcceptDeclaredResourceDictionarySource(string resource)
    {
        var projectRoot = CreateProjectPack(includePreview: true, baseKind: "contentControl");
        AddRuntimeMetadata(projectRoot, "sample", resource);
        using var trusted = TrustRuntimePacks(projectRoot, "sample");
        try
        {
            var result = await new UiBlueprintPreviewService(CreateRegistry(projectRoot)).PreviewAsync(
                new PreviewBlueprintRequest(Blueprint("sample.panel"), RestoreEnabled: false, KeepArtifacts: true));

            result.Valid.Should().BeTrue(string.Join(
                Environment.NewLine,
                result.Diagnostics.Select(diagnostic => diagnostic.Code + ": " + diagnostic.Message)));
            result.Diagnostics.Should().NotContain(diagnostic => diagnostic.Code == "UnsafePreviewResource");
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public async Task PreviewBlueprint_ShouldResolveWindowRootFromTheOwningSharedNamespacePack()
    {
        var projectRoot = CreateProjectPack(includePreview: true, baseKind: "contentControl");
        AddRuntimeMetadata(projectRoot, "sample");
        AddSharedNamespaceProjectPack(projectRoot);
        AddRuntimeMetadata(projectRoot, "other");
        MakeOtherPackOwnSharedWindowRoot(projectRoot);
        var previewRoot = CreateTempDirectory();
        using var trusted = TrustRuntimePacks(projectRoot, "sample", "other");
        try
        {
            var result = await new UiBlueprintPreviewService(CreateRegistry(projectRoot)).PreviewAsync(
                new PreviewBlueprintRequest(
                    SharedWindowRootBlueprint(),
                    RestoreEnabled: false,
                    TemporaryRoot: previewRoot,
                    KeepArtifacts: true));

            result.Valid.Should().BeTrue(string.Join(
                Environment.NewLine,
                result.Diagnostics.Select(diagnostic => diagnostic.Code + ": " + diagnostic.Message)));
            File.ReadAllText(Path.Combine(previewRoot, "MainWindow.xaml")).TrimStart()
                .Should().StartWith("<sample:Shell");
            File.ReadAllText(Path.Combine(previewRoot, "MainWindow.xaml.cs"))
                .Should().Contain("public partial class WpfDevToolsPreviewWindow_")
                .And.Contain(" : Sample.Controls.Shell");
        }
        finally
        {
            DeleteDirectory(projectRoot);
            DeleteDirectory(previewRoot);
        }
    }

    [Fact]
    public async Task PreviewBlueprint_ShouldRejectRuntimeAndStructuralMappingsForTheSameNamespaceUri()
    {
        var projectRoot = CreateProjectPack(includePreview: true, baseKind: "contentControl");
        AddRuntimeMetadata(projectRoot, "sample");
        AddSharedNamespaceProjectPack(projectRoot);
        AddRuntimeMetadata(projectRoot, "other");
        GiveOtherPackDistinctPrefix(projectRoot);
        using var trusted = TrustRuntimePacks(projectRoot, "sample");
        try
        {
            var result = await new UiBlueprintPreviewService(CreateRegistry(projectRoot)).PreviewAsync(
                new PreviewBlueprintRequest(SharedNamespaceBlueprint(), RestoreEnabled: false));

            result.Success.Should().BeFalse();
            result.Diagnostics.Should().ContainSingle(diagnostic =>
                diagnostic.Code == "PreviewNamespaceUriConflict");
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    [Theory]
    [InlineData("x")]
    [InlineData("xml")]
    [InlineData("bad prefix")]
    public void PreviewContract_ShouldRejectReservedOrInvalidXmlPrefixes(string prefix)
    {
        var projectRoot = CreateProjectPack(includePreview: true, baseKind: "contentControl");
        try
        {
            ReplacePackPrefix(projectRoot, "sample", prefix);
            var result = new UiPackPreviewContractGenerator(CreateRegistry(projectRoot)).Generate(
                Blueprint("sample.panel"),
                $"<{prefix}:Panel />");

            result.Success.Should().BeFalse();
            result.Diagnostics.Should().Contain(diagnostic =>
                diagnostic.Code == "PackXmlNamespaceInvalid"
                || diagnostic.Code == "PreviewContractInvalid");
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public async Task PreviewBlueprint_ShouldLoadDuplicateRuntimeResourcesOnlyOnce()
    {
        var projectRoot = CreateProjectPack(includePreview: true, baseKind: "contentControl");
        AddRuntimeMetadata(projectRoot, "sample");
        AddSharedNamespaceProjectPack(projectRoot);
        AddRuntimeMetadata(projectRoot, "other");
        RemoveDuplicateSharedPreviewTypes(projectRoot);
        var previewRoot = CreateTempDirectory();
        using var trusted = TrustRuntimePacks(projectRoot, "sample", "other");
        try
        {
            var result = await new UiBlueprintPreviewService(CreateRegistry(projectRoot)).PreviewAsync(
                new PreviewBlueprintRequest(
                    SharedNamespaceBlueprint(),
                    RestoreEnabled: false,
                    TemporaryRoot: previewRoot,
                    KeepArtifacts: true));

            result.Valid.Should().BeTrue();
            CountOccurrences(File.ReadAllText(Path.Combine(previewRoot, "App.xaml")), "<sample:Theme />")
                .Should().Be(1);
        }
        finally
        {
            DeleteDirectory(projectRoot);
            DeleteDirectory(previewRoot);
        }
    }

    [Fact]
    public async Task PreviewBlueprint_ShouldRejectConflictingHashesForTheSameRuntimePackage()
    {
        var projectRoot = CreateProjectPack(includePreview: true, baseKind: "contentControl");
        AddRuntimeMetadata(projectRoot, "sample");
        AddSharedNamespaceProjectPack(projectRoot);
        RemoveDuplicateSharedPreviewTypes(projectRoot);
        SetRuntimePackage(
            projectRoot,
            "other",
            "[1.0.0]",
            "dliiNNv0MkE4tcVjwTOhb3gIjN0Eqo4appivHiyBN7W3oaeGaBlrOvjkBn88Vk6zSQFnTd3FFchdkdPOIaHGXg==");
        SetRuntimePackageId(projectRoot, "other", "Sample.Runtime");
        using var trusted = TrustRuntimePacks(projectRoot, "sample", "other");
        try
        {
            var result = await new UiBlueprintPreviewService(CreateRegistry(projectRoot)).PreviewAsync(
                new PreviewBlueprintRequest(SharedNamespaceBlueprint(), RestoreEnabled: false));

            result.Success.Should().BeFalse();
            result.Diagnostics.Should().ContainSingle(diagnostic =>
                diagnostic.Code == "PreviewPackageIdentityConflict");
            result.RuntimePackApprovalReviews.Should().HaveCount(2);
        }
        finally
        {
            DeleteDirectory(projectRoot);
        }
    }

    [Fact]
    public async Task PreviewBlueprint_ShouldUseAPreviewLocalNuGetPackageCache()
    {
        var projectRoot = CreateProjectPack(includePreview: true, baseKind: "contentControl");
        AddRuntimeMetadata(projectRoot, "sample");
        var previewRoot = CreateTempDirectory();
        using var trusted = TrustRuntimePacks(projectRoot, "sample");
        try
        {
            _ = await new UiBlueprintPreviewService(CreateRegistry(projectRoot)).PreviewAsync(
                new PreviewBlueprintRequest(
                    Blueprint("sample.panel"),
                    RestoreEnabled: false,
                    TemporaryRoot: previewRoot,
                    KeepArtifacts: true));

            File.ReadAllText(Path.Combine(previewRoot, "PreviewHost.csproj"))
                .Should().Contain("<RestorePackagesPath>$(MSBuildThisFileDirectory).nuget\\packages</RestorePackagesPath>");
        }
        finally
        {
            DeleteDirectory(projectRoot);
            DeleteDirectory(previewRoot);
        }
    }

    [Fact]
    [Trait("Category", "ComposerCompile")]
    public async Task PreviewBlueprint_ShouldRejectRestoredPackageWhoseHashDoesNotMatchThePack()
    {
        var projectRoot = CreateWpfUiProjectPackWithHash(
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA==");
        var previewRoot = CreateTempDirectory();
        using var trusted = TrustRuntimePacks(projectRoot, "wpfui");
        try
        {
            var result = await new UiBlueprintPreviewService(CreateRegistry(projectRoot)).PreviewAsync(
                new PreviewBlueprintRequest(
                    WpfUiBlueprint(),
                    RestoreEnabled: true,
                    TemporaryRoot: previewRoot,
                    KeepArtifacts: true));

            result.BuildSucceeded.Should().BeFalse();
            result.Diagnostics.Should().Contain(diagnostic =>
                diagnostic.Code == "PreviewPackageHashMismatch");
        }
        finally
        {
            DeleteDirectory(projectRoot);
            DeleteDirectory(previewRoot);
        }
    }

    [Fact]
    public void PreviewPackageValidation_ShouldRejectUndeclaredTransitivePackages()
    {
        const string hash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA==";
        var previewRoot = CreateTempDirectory();
        try
        {
            WritePreviewPackageHash(previewRoot, "declared.package", "1.0.0", hash);
            WritePreviewPackageHash(previewRoot, "transitive.package", "2.0.0", hash);

            var diagnostics = UiPreviewRuntimeDependencyPolicy.ValidateRestoredPackages(
                previewRoot,
                [new PreviewRuntimeNuGetPackage("Declared.Package", "[1.0.0]", "1.0.0", hash)]);

            diagnostics.Should().ContainSingle(diagnostic =>
                diagnostic.Code == "PreviewPackageClosureMismatch");
        }
        finally
        {
            DeleteDirectory(previewRoot);
        }
    }

    [Theory]
    [InlineData("cancelled")]
    [InlineData("start-failed")]
    [InlineData("timed-out")]
    public void PreviewResult_ShouldNotClaimVisualEvidenceWhenHostDidNotLoad(string hostStatus)
    {
        var result = new PreviewBlueprintResult(
            true,
            true,
            true,
            true,
            string.Empty,
            "<Window />",
            [],
            new PreviewHostResult(hostStatus, Started: true, ViewLoaded: false))
        {
            UsesRuntimeDependencies = true
        };

        result.VisualFidelity.Should().Be("not-available");
        result.VisualComparisonChecklist.Should().OnlyContain(item =>
            !item.Preview.Contains("loads", StringComparison.OrdinalIgnoreCase)
            && !item.Preview.Contains("measures", StringComparison.OrdinalIgnoreCase));
    }

    private static void SetRuntimePackage(
        string projectRoot,
        string packId,
        string versionRange,
        string? contentHash)
    {
        var packPath = Path.Combine(projectRoot, ".wpfdevtools", "packs", packId, "1.0.0", "pack.json");
        var pack = JsonNode.Parse(File.ReadAllText(packPath))!.AsObject();
        var package = pack["nugetPackages"]!.AsArray()[0]!.AsObject();
        package["versionRange"] = versionRange;
        if (contentHash is null)
        {
            package.Remove("contentHash");
        }
        else
        {
            package["contentHash"] = contentHash;
        }

        File.WriteAllText(packPath, pack.ToJsonString());
    }

    private static void SetRuntimePackageId(string projectRoot, string packId, string packageId)
    {
        var packPath = Path.Combine(projectRoot, ".wpfdevtools", "packs", packId, "1.0.0", "pack.json");
        var pack = JsonNode.Parse(File.ReadAllText(packPath))!.AsObject();
        pack["nugetPackages"]!.AsArray()[0]!["id"] = packageId;
        File.WriteAllText(packPath, pack.ToJsonString());
    }

    private static void MakeOtherPackOwnSharedWindowRoot(string projectRoot)
    {
        var packRoot = Path.Combine(projectRoot, ".wpfdevtools", "packs", "other", "1.0.0");
        var packPath = Path.Combine(packRoot, "pack.json");
        var pack = JsonNode.Parse(File.ReadAllText(packPath))!.AsObject();
        var types = pack["preview"]!["types"]!.AsObject();
        types.Clear();
        types["Shell"] = JsonNode.Parse("""{"baseKind":"window","properties":{}}""");
        File.WriteAllText(packPath, pack.ToJsonString());
        File.WriteAllText(
            Path.Combine(packRoot, "renderers", "xaml", "panel.xaml.sbn"),
            "<sample:Shell Title=\"Shared runtime shell\" />");
    }

    private static void GiveOtherPackDistinctPrefix(string projectRoot)
        => ReplacePackPrefix(projectRoot, "other", "other");

    private static void ReplacePackPrefix(string projectRoot, string packId, string prefix)
    {
        var packRoot = Path.Combine(projectRoot, ".wpfdevtools", "packs", packId, "1.0.0");
        var packPath = Path.Combine(packRoot, "pack.json");
        var pack = JsonNode.Parse(File.ReadAllText(packPath))!.AsObject();
        pack["xmlNamespaces"] = new JsonObject { [prefix] = "urn:sample-controls" };
        File.WriteAllText(packPath, pack.ToJsonString());
        var rendererPath = Path.Combine(packRoot, "renderers", "xaml", "panel.xaml.sbn");
        File.WriteAllText(
            rendererPath,
            File.ReadAllText(rendererPath).Replace("<sample:", $"<{prefix}:", StringComparison.Ordinal)
                .Replace("</sample:", $"</{prefix}:", StringComparison.Ordinal));
    }

    private static string CreateWpfUiProjectPackWithHash(string contentHash)
    {
        var projectRoot = CreateTempDirectory();
        var source = Path.Combine(
            TestRepositoryPaths.GetRepoFilePath("."),
            "packs",
            "builtin",
            "wpfui",
            "0.1.0");
        var destination = Path.Combine(projectRoot, ".wpfdevtools", "packs", "wpfui", "0.1.0");
        foreach (var sourceFile in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var destinationFile = Path.Combine(destination, Path.GetRelativePath(source, sourceFile));
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
            File.Copy(sourceFile, destinationFile);
        }

        var packPath = Path.Combine(destination, "pack.json");
        var pack = JsonNode.Parse(File.ReadAllText(packPath))!.AsObject();
        var package = pack["nugetPackages"]!.AsArray()[0]!.AsObject();
        package["versionRange"] = "[4.3.0]";
        package["contentHash"] = contentHash;
        File.WriteAllText(packPath, pack.ToJsonString());
        return projectRoot;
    }

    private static string WpfUiBlueprint()
        => """
            {
              "schemaVersion": "wpfdevtools.ui-blueprint.v1",
              "name": "HashCheck",
              "packs": [
                { "id": "core", "version": "0.1.0", "required": true, "role": "layout-pack" },
                { "id": "wpfui", "version": "0.1.0", "required": true, "role": "primary" }
              ],
              "primaryPack": "wpfui",
              "layout": { "kind": "wpfui.button", "properties": { "text": "Hash check" } }
            }
            """;

    private static string SharedWindowRootBlueprint()
        => """
            {
              "schemaVersion": "wpfdevtools.ui-blueprint.v1",
              "name": "SharedWindowRoot",
              "packs": [
                { "id": "sample", "version": "1.0.0", "required": true, "role": "primary" },
                { "id": "other", "version": "1.0.0", "required": true, "role": "extension" }
              ],
              "primaryPack": "sample",
              "layout": { "kind": "other.panel" }
            }
            """;

    private static void WritePreviewPackageHash(
        string previewRoot,
        string packageId,
        string version,
        string hash)
    {
        var packageRoot = Path.Combine(previewRoot, ".nuget", "packages", packageId, version);
        Directory.CreateDirectory(packageRoot);
        File.WriteAllText(Path.Combine(packageRoot, $"{packageId}.{version}.nupkg.sha512"), hash);
    }

    private static int CountOccurrences(string value, string needle)
        => (value.Length - value.Replace(needle, string.Empty, StringComparison.Ordinal).Length) / needle.Length;
}
