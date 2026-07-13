using System.Text.Json;
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

    [Fact]
    public void PublicOnlineInstaller_FullUninstall_ShouldRemoveEmptyInstallerOwnedInstallRoot()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(
                tempRoot,
                useSignedPayload: true,
                isolateArchiveContents: true);
            var signer = ReleaseScriptTestHarness.GetSignedPayloadSigner();
            ReleaseScriptTestHarness.WriteAdjacentReleaseMetadata(archivePath, signer.Thumbprint, signer.Subject);

            var standaloneRoot = Path.Combine(tempRoot, "public-entry");
            var standaloneInstaller = Path.Combine(standaloneRoot, "online-installer.ps1");
            var standaloneInstallerModules = Path.Combine(standaloneRoot, "installer");
            Directory.CreateDirectory(standaloneInstallerModules);
            File.Copy(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                standaloneInstaller,
                overwrite: true);
            ReleaseScriptTestHarness.CopyOnlineInstallerRuntimeBundle(standaloneRoot);
            File.Copy(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/online-installer.release-assets.ps1"),
                Path.Combine(standaloneInstallerModules, "online-installer.release-assets.ps1"),
                overwrite: true);

            var installRoot = Path.Combine(tempRoot, "install-root");
            var environment = new Dictionary<string, string?>(
                StandaloneInstallerRegressionTestSupport.CreatePublicStandaloneEnvironment(tempRoot))
            {
                ["PATH"] = string.Empty,
                ["WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT"] = signer.Thumbprint,
                ["WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT"] = signer.Subject
            };

            var install = ReleaseScriptTestHarness.RunPowerShellScript(
                standaloneInstaller,
                [
                    "-PackageArchivePath", archivePath,
                    "-TrustedReleaseMetadataDirectory", tempRoot,
                    "-InstallRoot", installRoot,
                    "-Client", "other",
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ],
                environment,
                timeout: TimeSpan.FromMinutes(2));

            install.ExitCode.Should().Be(0, install.Stderr);
            Directory.Exists(installRoot).Should().BeTrue("the installer creates the root before full-uninstall");

            var removal = ReleaseScriptTestHarness.RunPowerShellScript(
                standaloneInstaller,
                [
                    "-Action", "full-uninstall",
                    "-Architecture", "x64",
                    "-InstallRoot", installRoot,
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ],
                environment,
                timeout: TimeSpan.FromMinutes(2));

            removal.ExitCode.Should().Be(0, removal.Stderr);
            using var removalJson = JsonDocument.Parse(removal.Stdout);
            removalJson.RootElement.TryGetProperty("removedInstallRoots", out var removedRoots).Should().BeTrue(
                "public full-uninstall output should expose root cleanup evidence");
            removedRoots.EnumerateArray()
                .Select(root => root.GetString())
                .Should().Contain(installRoot);
            Directory.Exists(installRoot).Should().BeFalse(
                "public full-uninstall should remove the now-empty installer-owned root without relying on repo-local helper modules or stale hosted helpers");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }
}
