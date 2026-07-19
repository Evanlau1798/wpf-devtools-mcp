using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Tests.Unit.TestSupport;
using Xunit;

namespace WpfDevTools.Tests.Unit.Composer;

public sealed class ComposerBaselinePackTests
{
    private const string PackRoot = "packs/builtin/wpfui/0.1.0";
    private const string BaselineRoot = "packs/baselines/wpfui/0.1.0";

    private static readonly string[] ExpectedBlockKinds =
    [
        "wpfui.button",
        "wpfui.card",
        "wpfui.dataGrid",
        "wpfui.editorialCard",
        "wpfui.fluentWindow",
        "wpfui.navigationView",
        "wpfui.navigationViewDemo",
        "wpfui.navigationViewItem",
        "wpfui.navigationViewItemSeparator",
        "wpfui.numberBox",
        "wpfui.progressRing",
        "wpfui.symbolIcon",
        "wpfui.tabView",
        "wpfui.tabViewItem",
        "wpfui.textBlock",
        "wpfui.titleBar",
        "wpfui.toggleSwitch"
    ];

    [Fact]
    public void BuiltinWpfUiPack_ShouldFreezeCurrentSemanticBaseline()
    {
        using var pack = ReadJson(Path.Combine(PackRoot, "pack.json"));
        using var sourceLock = ReadJson(Path.Combine(PackRoot, "source.lock.json"));

        var packRoot = GetRepoFilePath(PackRoot);
        Directory.Exists(packRoot).Should().BeTrue();
        GetString(pack.RootElement, "schemaVersion").Should().Be("wpfdevtools.ui-pack.v1");
        GetString(pack.RootElement, "id").Should().Be("wpfui");
        GetString(pack.RootElement, "version").Should().Be("0.1.0");
        GetStringArray(pack.RootElement, "blocks").Should().BeEquivalentTo(ExpectedBlockKinds);

        Directory.GetFiles(Path.Combine(packRoot, "blocks"), "*.block.json")
            .Should().HaveCount(17);
        Directory.GetFiles(Path.Combine(packRoot, "renderers", "xaml"), "*.xaml.sbn")
            .Should().HaveCount(17);
        Directory.GetFiles(Path.Combine(packRoot, "recipes"), "*.recipe.json")
            .Should().HaveCount(4);
        Directory.GetFiles(Path.Combine(packRoot, "examples"), "*.ui.json")
            .Should().HaveCount(1);

        GetStringArray(sourceLock.RootElement.GetProperty("sources")[0], "paths")
            .Should().Equal(
                "src/Wpf.Ui",
                "src/Wpf.Ui/Controls/ControlAppearance.cs",
                "src/Wpf.Ui/Controls/SymbolRegular.cs");
        ExpectedBlockKinds.Should().NotContain(kind =>
            kind.Contains("gallery", StringComparison.OrdinalIgnoreCase)
            || kind.Contains("syntax", StringComparison.OrdinalIgnoreCase)
            || kind.Contains("tray", StringComparison.OrdinalIgnoreCase)
            || kind.Contains("template", StringComparison.OrdinalIgnoreCase)
            || kind.Contains("sample", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuiltinWpfUiPack_ShouldPreserveRequiredSlotKindRelationships()
    {
        foreach (var (blockKind, slotName, allowedKind) in RequiredSlotKinds)
        {
            using var block = ReadJson(Path.Combine(PackRoot, "blocks", $"{blockKind["wpfui.".Length..]}.block.json"));

            GetAllowedKinds(block.RootElement, slotName)
                .Should().Contain(allowedKind, $"{blockKind}.{slotName} must preserve the Composer baseline relationship");
        }
    }

    [Fact]
    public void BuiltinWpfUiBaseline_ShouldKeepReleaseReportsAndUtf8Json()
    {
        using var pack = ReadJson(Path.Combine(PackRoot, "pack.json"));
        using var sourceLock = ReadJson(Path.Combine(PackRoot, "source.lock.json"));
        using var validation = ReadJson(Path.Combine(BaselineRoot, "reports", "wpfui-0.1.0.validation-report.json"));
        using var coverage = ReadJson(Path.Combine(BaselineRoot, "reports", "wpfui-0.1.0.coverage-audit.json"));
        using var readiness = ReadJson(Path.Combine(BaselineRoot, "reports", "wpfui-0.1.0.readiness.json"));
        using var archive = ZipFile.OpenRead(GetRepoFilePath(Path.Combine(BaselineRoot, "archives", "wpfui-0.1.0.zip")));
        var reportPath = GetRepoFilePath(Path.Combine(BaselineRoot, "wpfui-0.1.0-generation-report.txt"));
        var report = File.ReadAllText(reportPath);

        validation.RootElement.GetProperty("valid").GetBoolean().Should().BeTrue();
        validation.RootElement.GetProperty("strict").GetBoolean().Should().BeTrue();
        coverage.RootElement.GetProperty("valid").GetBoolean().Should().BeTrue();
        readiness.RootElement.GetProperty("valid").GetBoolean().Should().BeTrue();
        GetString(readiness.RootElement, "requestedLevel").Should().Be("release");
        report.Should().Contain("check_pack_readiness.py");
        report.Should().Contain("## Beta 60 Maintenance Commands and Results");
        report.Should().Contain(
            "python <extension-creator-root>/scripts/validate_pack.py packs/builtin/wpfui/0.1.0 --strict");
        report.Should().Contain(
            "python <extension-creator-root>/scripts/audit_pack_coverage.py packs/builtin/wpfui/0.1.0 --source-inventory <generation-workspace>/out/inventory/wpfui.source-inventory.json");
        var coverageWarningCode = coverage.RootElement.GetProperty("warnings")[0].GetProperty("code").GetString();
        report.Should().Contain($"warning: `{coverageWarningCode}`");
        archive.Entries.Should().OnlyContain(entry =>
            entry.FullName.StartsWith("wpfui/0.1.0/", StringComparison.Ordinal)
            && !entry.FullName.Contains('\\'));
        archive.Entries.Select(entry => entry.FullName).Should().Contain("wpfui/0.1.0/pack.json");
        GetStringArray(pack.RootElement, "recipes").Should().HaveCount(4);
        readiness.RootElement.GetProperty("summary").GetProperty("recipeCount").GetInt32().Should().Be(4);
        coverage.RootElement.GetProperty("summary").GetProperty("recipeCount").GetInt32().Should().Be(4);
        GetStringArray(readiness.RootElement.GetProperty("summary"), "sourceLockPaths").Should().Equal(
            GetStringArray(sourceLock.RootElement.GetProperty("sources")[0], "paths"));
        coverage.RootElement.GetProperty("summary").GetProperty("rendererTokens")
            .GetProperty("identity.attributes").GetInt32().Should().Be(1);
        var builtInRecipeFiles = Directory.GetFiles(Path.Combine(GetRepoFilePath(PackRoot), "recipes"), "*.recipe.json")
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var archiveRecipeFiles = archive.Entries
            .Where(entry => entry.FullName.StartsWith("wpfui/0.1.0/recipes/", StringComparison.Ordinal))
            .Select(entry => entry.FullName.Split('/').Last())
            .Order(StringComparer.Ordinal)
            .ToArray();
        archiveRecipeFiles.Should().Equal(builtInRecipeFiles);

        foreach (var path in Directory.EnumerateFiles(GetRepoFilePath("packs"), "*.json", SearchOption.AllDirectories))
        {
            File.ReadAllBytes(path).Take(3).Should().NotEqual([0xEF, 0xBB, 0xBF]);
        }
    }

    [Fact]
    public void BuiltinWpfUiBaselineArchive_ShouldMatchBuiltinPackFiles()
    {
        using var archive = ZipFile.OpenRead(GetRepoFilePath(Path.Combine(BaselineRoot, "archives", "wpfui-0.1.0.zip")));
        var packRoot = GetRepoFilePath(PackRoot);
        var archivePrefix = "wpfui/0.1.0/";

        var packHashes = Directory.EnumerateFiles(packRoot, "*", SearchOption.AllDirectories)
            .ToDictionary(
                path => Path.GetRelativePath(packRoot, path).Replace('\\', '/'),
                path =>
                {
                    using var stream = File.OpenRead(path);
                    return ComputeNormalizedTextHash(stream);
                },
                StringComparer.Ordinal);

        var archiveHashes = archive.Entries
            .Where(entry => !string.IsNullOrEmpty(entry.Name))
            .ToDictionary(
                entry =>
                {
                    entry.FullName.Should().StartWith(archivePrefix);
                    return entry.FullName[archivePrefix.Length..];
                },
                entry =>
                {
                    using var stream = entry.Open();
                    return ComputeNormalizedTextHash(stream);
                },
                StringComparer.Ordinal);

        archiveHashes.Keys.Should().BeEquivalentTo(packHashes.Keys);
        foreach (var (relativePath, archiveHash) in archiveHashes)
        {
            archiveHash.Should().Equal(packHashes[relativePath], $"baseline archive entry {relativePath} must match the built-in pack file");
        }
    }

    [Fact]
    public void BuiltinWpfUiBaseline_ShouldNotContainLocalMachinePaths()
    {
        var metadataFiles = Directory.EnumerateFiles(GetRepoFilePath(BaselineRoot), "*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".json", StringComparison.Ordinal) || path.EndsWith(".txt", StringComparison.Ordinal))
            .Concat([GetRepoFilePath(Path.Combine(PackRoot, "source.lock.json"))]);

        foreach (var path in metadataFiles)
        {
            File.ReadAllText(path).Should().NotMatchRegex(
                @"(?<![A-Za-z])[A-Za-z]:[\\/]",
                $"{Path.GetRelativePath(GetRepoFilePath("."), path)} should be portable committed metadata");
        }
    }

    private static readonly (string BlockKind, string SlotName, string AllowedKind)[] RequiredSlotKinds =
    [
        ("wpfui.navigationView", "items", "wpfui.navigationViewItem"),
        ("wpfui.navigationView", "items", "wpfui.navigationViewItemSeparator"),
        ("wpfui.navigationViewItem", "icon", "wpfui.symbolIcon"),
        ("wpfui.tabView", "items", "wpfui.tabViewItem"),
        ("wpfui.fluentWindow", "titleBar", "wpfui.titleBar"),
    ];

    private static JsonDocument ReadJson(string relativePath)
        => JsonDocument.Parse(File.ReadAllText(GetRepoFilePath(relativePath)));

    private static byte[] ComputeNormalizedTextHash(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var normalizedText = reader.ReadToEnd()
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
        return SHA256.HashData(Encoding.UTF8.GetBytes(normalizedText));
    }

    private static string GetRepoFilePath(string relativePath)
        => TestRepositoryPaths.GetRepoFilePath(relativePath);

    private static string GetString(JsonElement element, string propertyName)
        => element.GetProperty(propertyName).GetString()!;

    private static string[] GetStringArray(JsonElement element, string propertyName)
        => element.GetProperty(propertyName)
            .EnumerateArray()
            .Select(item => item.GetString()!)
            .ToArray();

    private static string[] GetAllowedKinds(JsonElement block, string slotName)
        => block.GetProperty("slots")
            .GetProperty(slotName)
            .GetProperty("allowedKinds")
            .EnumerateArray()
            .Select(item => item.GetString()!)
            .ToArray();
}
