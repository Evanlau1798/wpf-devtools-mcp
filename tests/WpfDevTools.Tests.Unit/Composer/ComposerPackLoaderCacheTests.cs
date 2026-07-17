using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Contracts;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Mcp.Server.Composer.Rendering;

namespace WpfDevTools.Tests.Unit.Composer;

[Collection("ComposerPackLoaderCache")]
public sealed class ComposerPackLoaderCacheTests
{
    [Fact]
    public void Load_ShouldReturnCachedPackWhenFingerprintIsUnchanged()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var packRoot = CreateMinimalPack(tempRoot);
            ComposerPackLoader.ClearCacheForTests();

            var first = ComposerPackLoader.Load(packRoot);
            var second = ComposerPackLoader.Load(packRoot);

            second.Should().BeSameAs(first);
        }
        finally
        {
            ComposerPackLoader.ClearCacheForTests();
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void Load_ShouldInvalidateCacheWhenContentChangesWithPreservedMetadata()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var packRoot = CreateMinimalPack(tempRoot);
            var blockPath = Path.Combine(packRoot, "blocks", "text.block.json");
            ComposerPackLoader.ClearCacheForTests();
            var first = ComposerPackLoader.LoadWithFingerprint(packRoot);
            var originalWriteTime = File.GetLastWriteTimeUtc(blockPath);

            WriteBlock(packRoot, "Fake");
            File.SetLastWriteTimeUtc(blockPath, originalWriteTime);
            var second = ComposerPackLoader.LoadWithFingerprint(packRoot);

            second.FromCache.Should().BeFalse();
            second.Fingerprint.Should().NotBe(first.Fingerprint);
            second.Pack.Blocks.Single().DisplayName.Should().Be("Fake");
        }
        finally
        {
            ComposerPackLoader.ClearCacheForTests();
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void Load_ShouldNotReadUntrackedPayloadForCacheHit()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var packRoot = CreateMinimalPack(tempRoot);
            var payloadPath = Path.Combine(packRoot, "payload.bin");
            File.WriteAllText(payloadPath, "unchanged");
            ComposerPackLoader.ClearCacheForTests();
            var first = ComposerPackLoader.Load(packRoot);

            using var locked = new FileStream(payloadPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var second = ComposerPackLoader.Load(packRoot);

            second.Should().BeSameAs(first);
        }
        finally
        {
            ComposerPackLoader.ClearCacheForTests();
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void Load_ShouldIgnoreNonContractFilesForCacheInvalidation()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var packRoot = CreateMinimalPack(tempRoot);
            ComposerPackLoader.ClearCacheForTests();
            var first = ComposerPackLoader.Load(packRoot);

            File.WriteAllText(Path.Combine(packRoot, "payload.bin"), Guid.NewGuid().ToString("N"));
            var second = ComposerPackLoader.Load(packRoot);

            second.Should().BeSameAs(first);
        }
        finally
        {
            ComposerPackLoader.ClearCacheForTests();
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void GetFingerprint_ShouldUseUnambiguousFraming()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var firstRoot = Path.Combine(tempRoot, "first");
            var secondRoot = Path.Combine(tempRoot, "second");
            Directory.CreateDirectory(firstRoot);
            Directory.CreateDirectory(secondRoot);
            File.WriteAllBytes(Path.Combine(firstRoot, "pack.json"), [(byte)'b']);
            File.WriteAllBytes(Path.Combine(firstRoot, "source.lock.json"), []);
            File.WriteAllBytes(Path.Combine(secondRoot, "pack.json"), [(byte)'b', 0, (byte)'c', 0]);

            ComposerPackLoader.GetFingerprint(firstRoot).Should()
                .NotBe(ComposerPackLoader.GetFingerprint(secondRoot));
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void Load_ShouldInvalidateCacheWhenBlockDefinitionChanges()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var packRoot = CreateMinimalPack(tempRoot);
            ComposerPackLoader.ClearCacheForTests();
            var first = ComposerPackLoader.Load(packRoot);

            WriteBlock(packRoot, "Changed Text");

            var second = ComposerPackLoader.Load(packRoot);

            second.Should().NotBeSameAs(first);
            second.Blocks.Single().DisplayName.Should().Be("Changed Text");
        }
        finally
        {
            ComposerPackLoader.ClearCacheForTests();
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void Load_ShouldNotReturnStaleCacheWhenPackBecomesCorrupt()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var packRoot = CreateMinimalPack(tempRoot);
            ComposerPackLoader.ClearCacheForTests();
            ComposerPackLoader.Load(packRoot).Blocks.Should().ContainSingle();
            File.WriteAllText(Path.Combine(packRoot, "blocks", "text.block.json"), "{");

            var act = () => ComposerPackLoader.Load(packRoot);

            act.Should().Throw<JsonException>();
        }
        finally
        {
            ComposerPackLoader.ClearCacheForTests();
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void RendererTemplateLoader_ShouldInvalidateCacheWhenTemplateChanges()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var packRoot = CreateMinimalPack(tempRoot);
            ComposerPackLoader.ClearCacheForTests();
            var registry = new PackRegistry(Path.Combine(tempRoot, "builtin"), tempRoot);
            var loader = new RendererTemplateLoader(registry);
            loader.Load("sample.text", DeclaredSamplePack()).Template!.Content.Should().Contain("TextBlock");

            File.WriteAllText(Path.Combine(packRoot, "renderers", "xaml", "text.xaml.sbn"), "<Button />");

            var result = loader.Load("sample.text", DeclaredSamplePack());

            result.FromCache.Should().BeFalse();
            result.Template!.Content.Should().Contain("<Button />");
        }
        finally
        {
            ComposerPackLoader.ClearCacheForTests();
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void Load_ShouldRejectBlockKindOwnedByAnotherPack()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var packRoot = CreateMinimalPack(tempRoot);
            var blockPath = Path.Combine(packRoot, "blocks", "text.block.json");
            File.WriteAllText(
                blockPath,
                File.ReadAllText(blockPath).Replace("sample.text", "foreign.text", StringComparison.Ordinal));

            var act = () => ComposerPackLoader.LoadUncachedForValidation(packRoot);

            act.Should().Throw<InvalidDataException>()
                .WithMessage("*BlockKindOwnershipMismatch*foreign.text*sample*");
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void Load_ShouldRejectManifestAndBlockFileSetMismatch()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var packRoot = CreateMinimalPack(tempRoot);
            var manifestPath = Path.Combine(packRoot, "pack.json");
            File.WriteAllText(
                manifestPath,
                File.ReadAllText(manifestPath).Replace("sample.text", "sample.other", StringComparison.Ordinal));

            var act = () => ComposerPackLoader.LoadUncachedForValidation(packRoot);

            act.Should().Throw<InvalidDataException>()
                .WithMessage("*BlockManifestMismatch*sample.text*sample.other*");
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void Load_ShouldRejectInvertedSlotItemBounds()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var packRoot = CreateMinimalPack(tempRoot);
            var blockPath = Path.Combine(packRoot, "blocks", "text.block.json");
            File.WriteAllText(
                blockPath,
                File.ReadAllText(blockPath).Replace(
                    "\"slots\":{}",
                    "\"slots\":{\"children\":{\"allowedKinds\":[\"*\"],\"minItems\":2,\"maxItems\":1}}",
                    StringComparison.Ordinal));

            var act = () => ComposerPackLoader.LoadUncachedForValidation(packRoot);

            act.Should().Throw<InvalidDataException>()
                .WithMessage("*InvalidSlotItemBounds*children*minItems*maxItems*");
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void Load_ShouldAcceptOwnedBlockForDottedPackId()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var packRoot = CreateMinimalPack(tempRoot, "vendor.design");

            var pack = ComposerPackLoader.LoadUncachedForValidation(packRoot);

            pack.Blocks.Should().ContainSingle(block => block.Kind == "vendor.design.text");
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void Load_ShouldRejectAdjacencyAdvisoryWithoutRequiredCondition()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var packRoot = CreateMinimalPack(tempRoot);
            File.WriteAllText(
                Path.Combine(packRoot, "blocks", "text.block.json"),
                """
                {"schemaVersion":"wpfdevtools.ui-block.v1","kind":"sample.text","displayName":"Text","category":"text","authoringRoles":["copy-run"],"properties":{"gap":{"type":"string","format":"thickness","default":"0"}},"slots":{"items":{"allowedKinds":["sample.text"],"adjacencyAdvisory":{"childRole":"copy-run","itemSpacingProperty":"gap","message":"Add space.","repairSuggestion":"Set a gap."}}},"renderer":{"xamlTemplate":"renderers/xaml/text.xaml.sbn"},"sourceHints":[]}
                """);

            var action = () => ComposerPackLoader.LoadUncachedForValidation(packRoot);

            action.Should().Throw<InvalidDataException>()
                .WithMessage("*InvalidAdjacencyAdvisory*condition*spacing*");
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void Load_ShouldRejectClosedAdjacencyAdvisoryContractViolations()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var packRoot = CreateMinimalPack(tempRoot);
            var blockPath = Path.Combine(packRoot, "blocks", "text.block.json");
            File.WriteAllText(
                blockPath,
                """
                {"schemaVersion":"wpfdevtools.ui-block.v1","kind":"sample.text","displayName":"Text","category":"text","authoringRoles":["copy-run"],"properties":{"mode":{"type":"string","default":"Across"},"gap":{"type":"string","format":"thickness","default":"0"}},"slots":{"items":{"allowedKinds":["sample.text"],"adjacencyAdvisory":{"childRole":"copy-run","whenProperty":"mode","whenValues":["Across"],"itemSpacingProperty":"gap","childMargnProperty":"margin","message":"Add space.","repairSuggestion":"Set a gap."}}},"renderer":{"xamlTemplate":"renderers/xaml/text.xaml.sbn"},"sourceHints":[]}
                """);

            var unknownField = () => ComposerPackLoader.LoadUncachedForValidation(packRoot);

            unknownField.Should().Throw<InvalidDataException>()
                .WithMessage("*InvalidAdjacencyAdvisory*");

            File.WriteAllText(
                blockPath,
                """
                {"schemaVersion":"wpfdevtools.ui-block.v1","kind":"sample.text","displayName":"Text","category":"text","authoringRoles":["copy-run"],"properties":{"mode":{"type":"string","default":"Across"},"gap":{"type":"number","format":"thickness","default":0}},"slots":{"items":{"allowedKinds":["sample.text"],"adjacencyAdvisory":{"childRole":"copy-run","whenProperty":"mode","whenValues":["Across"],"itemSpacingProperty":"gap","message":"Add space.","repairSuggestion":"Set a gap."}}},"renderer":{"xamlTemplate":"renderers/xaml/text.xaml.sbn"},"sourceHints":[]}
                """);

            var nonStringSpacing = () => ComposerPackLoader.LoadUncachedForValidation(packRoot);

            nonStringSpacing.Should().Throw<InvalidDataException>()
                .WithMessage("*InvalidAdjacencyAdvisory*");
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    private static string CreateMinimalPack(string root, string packId = "sample")
    {
        var packRoot = Path.Combine(root, packId, "1.0.0");
        Directory.CreateDirectory(Path.Combine(packRoot, "blocks"));
        Directory.CreateDirectory(Path.Combine(packRoot, "recipes"));
        Directory.CreateDirectory(Path.Combine(packRoot, "examples"));
        Directory.CreateDirectory(Path.Combine(packRoot, "renderers", "xaml"));

        File.WriteAllText(
            Path.Combine(packRoot, "pack.json"),
            $$"""
            {"schemaVersion":"wpfdevtools.ui-pack.v1","id":"{{packId}}","displayName":"Sample Pack","version":"1.0.0","blocks":["{{packId}}.text"],"recipes":[]}
            """);
        File.WriteAllText(
            Path.Combine(packRoot, "source.lock.json"),
            """
            {"schemaVersion":"wpfdevtools.source-lock.v1","sources":[],"transformPolicy":{}}
            """);
        WriteBlock(packRoot, "Text", packId);
        File.WriteAllText(Path.Combine(packRoot, "renderers", "xaml", "text.xaml.sbn"), "<TextBlock />");
        File.WriteAllText(
            Path.Combine(packRoot, "install.manifest.json"),
            $$"""
            {"schemaVersion":"wpfdevtools.pack-install-manifest.v1","id":"{{packId}}","version":"1.0.0","scope":"project-local","path":"{{packRoot.Replace("\\", "\\\\")}}","enabled":true}
            """);
        return packRoot;
    }

    private static ComposerPackReference[] DeclaredSamplePack()
        => [new() { Id = "sample", Version = "1.0.0", Required = true }];

    private static void WriteBlock(string packRoot, string displayName, string packId = "sample")
        => File.WriteAllText(
            Path.Combine(packRoot, "blocks", "text.block.json"),
            $$"""
            {"schemaVersion":"wpfdevtools.ui-block.v1","kind":"{{packId}}.text","displayName":"{{displayName}}","category":"text","properties":{},"slots":{},"renderer":{"xamlTemplate":"renderers/xaml/text.xaml.sbn"},"sourceHints":[]}
            """);

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "wpfdevtools-composer-" + Guid.NewGuid().ToString("N"));
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
