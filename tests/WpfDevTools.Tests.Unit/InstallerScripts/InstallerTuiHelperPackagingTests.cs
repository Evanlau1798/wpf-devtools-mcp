using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class InstallerTuiHelperPackagingTests
{
    [Fact]
    public void OnlineInstallerScript_ShouldDeclareTuiSectionsHelperInBootstrapSources()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));

        content.Should().Contain("scripts/installer/Tui.Sections.ps1");
    }

    [Fact]
    public void InstallerHelperManifest_ShouldIncludeTuiSectionsHelper()
    {
        var manifestContent = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/installer-helpers.manifest.json"));

        manifestContent.Should().Contain("Tui.Sections.ps1");
    }

    [Fact]
    public void PackageArchive_ShouldContainTuiSectionsHelper()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var extractRoot = Path.Combine(tempRoot, "expanded");
            System.IO.Compression.ZipFile.ExtractToDirectory(archivePath, extractRoot);

            File.Exists(Path.Combine(extractRoot, "bin", "installer", "Tui.Sections.ps1")).Should().BeTrue();
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }
}
