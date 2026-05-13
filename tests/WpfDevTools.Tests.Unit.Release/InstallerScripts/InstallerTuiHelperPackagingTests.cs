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

        var helperEntries = manifest.RootElement.GetProperty("helperFiles").EnumerateArray().ToArray();
        helperEntries.Should().NotBeEmpty();

        foreach (var entry in helperEntries)
        {
            entry.ValueKind.Should().Be(JsonValueKind.Object);
            entry.GetProperty("path").GetString().Should().NotBeNullOrWhiteSpace();
            entry.GetProperty("sha256").GetString().Should().MatchRegex("^[a-f0-9]{64}$");
            entry.GetProperty("sizeBytes").GetInt64().Should().BePositive();
        }
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
}
