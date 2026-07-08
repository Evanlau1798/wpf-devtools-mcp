using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.Composer.Contracts;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Mcp.Server.Composer.Rendering;

namespace WpfDevTools.Tests.Unit.Composer;

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
    public void Load_ShouldNotReadUnchangedPackFileContentForCacheHit()
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
    public void GetFingerprint_ShouldUseUnambiguousFraming()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var firstRoot = Path.Combine(tempRoot, "first");
            var secondRoot = Path.Combine(tempRoot, "second");
            Directory.CreateDirectory(firstRoot);
            Directory.CreateDirectory(secondRoot);
            File.WriteAllBytes(Path.Combine(firstRoot, "a"), [(byte)'b']);
            File.WriteAllBytes(Path.Combine(firstRoot, "c"), []);
            File.WriteAllBytes(Path.Combine(secondRoot, "a"), [(byte)'b', 0, (byte)'c', 0]);

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

    private static string CreateMinimalPack(string root)
    {
        var packRoot = Path.Combine(root, "sample", "1.0.0");
        Directory.CreateDirectory(Path.Combine(packRoot, "blocks"));
        Directory.CreateDirectory(Path.Combine(packRoot, "recipes"));
        Directory.CreateDirectory(Path.Combine(packRoot, "examples"));
        Directory.CreateDirectory(Path.Combine(packRoot, "renderers", "xaml"));

        File.WriteAllText(
            Path.Combine(packRoot, "pack.json"),
            """
            {"schemaVersion":"wpfdevtools.ui-pack.v1","id":"sample","displayName":"Sample Pack","version":"1.0.0","blocks":["sample.text"],"recipes":[]}
            """);
        File.WriteAllText(
            Path.Combine(packRoot, "source.lock.json"),
            """
            {"schemaVersion":"wpfdevtools.source-lock.v1","sources":[],"transformPolicy":{}}
            """);
        WriteBlock(packRoot, "Text");
        File.WriteAllText(Path.Combine(packRoot, "renderers", "xaml", "text.xaml.sbn"), "<TextBlock />");
        File.WriteAllText(
            Path.Combine(packRoot, "install.manifest.json"),
            $$"""
            {"schemaVersion":"wpfdevtools.pack-install-manifest.v1","id":"sample","version":"1.0.0","scope":"project","path":"{{packRoot.Replace("\\", "\\\\")}}","enabled":true}
            """);
        return packRoot;
    }

    private static ComposerPackReference[] DeclaredSamplePack()
        => [new() { Id = "sample", Version = "1.0.0", Required = true }];

    private static void WriteBlock(string packRoot, string displayName)
        => File.WriteAllText(
            Path.Combine(packRoot, "blocks", "text.block.json"),
            $$"""
            {"schemaVersion":"wpfdevtools.ui-block.v1","kind":"sample.text","displayName":"{{displayName}}","category":"text","properties":{},"slots":{},"renderer":{"xamlTemplate":"renderers/xaml/text.xaml.sbn"},"sourceHints":[]}
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
