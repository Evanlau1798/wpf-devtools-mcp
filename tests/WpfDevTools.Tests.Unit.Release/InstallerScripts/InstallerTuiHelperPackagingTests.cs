using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class InstallerTuiHelperPackagingTests
{
    [Fact]
    public void OnlineInstallerScript_ShouldDeclarePseudoWindowHelpersInBootstrapSources()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));

        content.Should().Contain("scripts/installer/Tui.State.ps1");
        content.Should().Contain("scripts/installer/Tui.PathEditor.ps1");
        content.Should().Contain("scripts/installer/Tui.Window.ps1");
        content.Should().Contain("scripts/installer/Tui.Presenters.ps1");
        content.Should().Contain("scripts/installer/Tui.Sections.ps1");
    }

    [Fact]
    public void InstallerHelperManifest_ShouldIncludePseudoWindowHelpers()
    {
        var manifestContent = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/installer-helpers.manifest.json"));

        manifestContent.Should().Contain("Tui.State.ps1");
        manifestContent.Should().Contain("Tui.PathEditor.ps1");
        manifestContent.Should().Contain("Tui.Window.ps1");
        manifestContent.Should().Contain("Tui.Presenters.ps1");
        manifestContent.Should().Contain("Tui.Sections.ps1");
    }

    [Fact]
    public void InstallerHelperManifest_ShouldDeclareDigestMetadataForEveryHelper()
    {
        using var manifest = JsonDocument.Parse(
            File.ReadAllText(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/installer-helpers.manifest.json")));
        var helperRoot = ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer");

        var helperEntries = manifest.RootElement.GetProperty("helperFiles").EnumerateArray().ToArray();
        helperEntries.Should().NotBeEmpty();

        foreach (var entry in helperEntries)
        {
            entry.ValueKind.Should().Be(JsonValueKind.Object);
            var helperPath = entry.GetProperty("path").GetString();
            helperPath.Should().NotBeNullOrWhiteSpace();
            entry.GetProperty("sha256").GetString().Should().MatchRegex("^[a-f0-9]{64}$");

            var normalizedBytes = NormalizeCrLfToLf(File.ReadAllBytes(Path.Combine(helperRoot, helperPath!)));
            entry.GetProperty("sizeBytes").GetInt64().Should().Be(normalizedBytes.Length);
            entry.GetProperty("sha256").GetString().Should().Be(ComputeSha256(normalizedBytes));
        }
    }

    [Fact]
    public void InstallerHelperBundle_ShouldPinGitLineEndingsForManifestIntegrity()
    {
        var attributesPath = ReleaseScriptTestHarness.GetRepoFilePath(".gitattributes");

        File.Exists(attributesPath).Should().BeTrue();
        var attributes = File.ReadAllLines(attributesPath)
            .Select(static line => line.Trim())
            .Where(static line => line.Length > 0 && !line.StartsWith('#'))
            .ToArray();

        attributes.Should().Contain("/scripts/installer/** text eol=lf");
        attributes.Should().Contain("/scripts/online-installer.ps1 text eol=lf");
    }

    [Fact]
    public void PackageArchive_ShouldContainPseudoWindowHelpers()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var extractRoot = Path.Combine(tempRoot, "expanded");
            System.IO.Compression.ZipFile.ExtractToDirectory(archivePath, extractRoot);

            File.Exists(Path.Combine(extractRoot, "bin", "installer", "Tui.State.ps1")).Should().BeTrue();
            File.Exists(Path.Combine(extractRoot, "bin", "installer", "Tui.PathEditor.ps1")).Should().BeTrue();
            File.Exists(Path.Combine(extractRoot, "bin", "installer", "Tui.Window.ps1")).Should().BeTrue();
            File.Exists(Path.Combine(extractRoot, "bin", "installer", "Tui.Presenters.ps1")).Should().BeTrue();
            File.Exists(Path.Combine(extractRoot, "bin", "installer", "Tui.Sections.ps1")).Should().BeTrue();
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void ReleasePackagingScript_ShouldCopyManifestDeclaredHelpersInsteadOfWildcardDirectoryContents()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Publish-Release.ps1"));

        content.Should().Contain("Get-InstallerHelperFiles");
        content.Should().Contain("Copy-InstallerHelperFiles");
        content.Should().NotContain("Copy-DirectoryContents -Source (Join-Path $repoRoot 'scripts\\installer')");
    }

    [Fact]
    public void ReleaseScriptHarness_ShouldBuildArchivesFromInstallerHelperManifest()
    {
        var content = ReleaseScriptHarnessSource.ReadAll();

        content.Should().Contain("installer-helpers.manifest.json");
        content.Should().Contain("GetInstallerHelperFiles");
        content.Should().NotContain("Directory.GetFiles(");
    }

    private static byte[] NormalizeCrLfToLf(byte[] bytes)
    {
        using var output = new MemoryStream(bytes.Length);
        for (var index = 0; index < bytes.Length; index++)
        {
            if (bytes[index] == '\r' && index + 1 < bytes.Length && bytes[index + 1] == '\n')
            {
                continue;
            }

            output.WriteByte(bytes[index]);
        }

        return output.ToArray();
    }

    private static string ComputeSha256(byte[] bytes)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
