using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class OnlineInstallerModularizationTests
{
    [Fact]
    public void OnlineInstaller_ShouldLoadReleaseAssetSubsystemFromInstallerModule()
    {
        var root = FindRepoRoot();
        var installerPath = Path.Combine(root, "scripts", "online-installer.ps1");
        var modulePath = Path.Combine(root, "scripts", "installer", "online-installer.release-assets.ps1");
        var exceptionPath = Path.Combine(
            root,
            "tests",
            "WpfDevTools.Tests.Unit",
            "Documentation",
            "LineLimitExceptions.txt");

        File.Exists(modulePath).Should().BeTrue(
            "release discovery, checksum lookup, and local archive trust logic should be split out of the installer entrypoint");

        var installer = File.ReadAllText(installerPath);
        installer.Should().Contain("online-installer.release-assets.ps1");
        installer.Should().NotContain("function Get-ReleaseAssetName");
        installer.Should().NotContain("function Assert-LocalPackageArchiveTrustedForHelperBootstrap");

        File.ReadLines(modulePath).Count().Should().BeLessThanOrEqualTo(500);
        var exceptions = File.ReadAllText(exceptionPath);
        exceptions.Should().Contain("scripts/online-installer.ps1");
        exceptions.Should().NotContain("do not split in this remediation loop");
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "WpfDevTools.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
