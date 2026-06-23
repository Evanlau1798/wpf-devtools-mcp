using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class InstallerFullUninstallRootCleanupTests
{
    [Fact]
    public void OnlineInstaller_FullUninstall_ShouldRemoveEmptyInstallerOwnedInstallRoot()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");

            var install = StandaloneInstallerRegressionTestSupport.RunRepoInstaller(
                tempRoot,
                [
                    "-PackageArchivePath", archivePath,
                    "-InstallRoot", installRoot,
                    "-Client", "other",
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ]);

            install.ExitCode.Should().Be(0, install.Stderr);
            Directory.Exists(installRoot).Should().BeTrue("the installer creates the root before full-uninstall");

            var removal = StandaloneInstallerRegressionTestSupport.RunRepoInstaller(
                tempRoot,
                [
                    "-Action", "full-uninstall",
                    "-Architecture", "x64",
                    "-InstallRoot", installRoot,
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ]);

            removal.ExitCode.Should().Be(0, removal.Stderr);
            Directory.Exists(installRoot).Should().BeFalse("full-uninstall should remove the now-empty installer-owned root");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }
}
