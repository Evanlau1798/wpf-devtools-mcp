using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class InstallerFullUninstallRootCleanupTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OnlineInstaller_FullUninstallWithExplicitRoot_ShouldPreserveOtherInstallRoot(
        bool useStandaloneFallback)
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var packageRootA = Path.Combine(tempRoot, "package-a");
            var packageRootB = Path.Combine(tempRoot, "package-b");
            Directory.CreateDirectory(packageRootA);
            Directory.CreateDirectory(packageRootB);
            var archiveA = ReleaseScriptTestHarness.CreatePackageArchive(packageRootA, "x64");
            var archiveB = ReleaseScriptTestHarness.CreatePackageArchive(packageRootB, "x86");
            var installRootA = Path.Combine(tempRoot, "install-a");
            var installRootB = Path.Combine(tempRoot, "install-b");

            var installA = StandaloneInstallerRegressionTestSupport.RunRepoInstaller(
                tempRoot,
                [
                    "-PackageArchivePath", archiveA,
                    "-InstallRoot", installRootA,
                    "-Client", "other",
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ]);
            var installB = StandaloneInstallerRegressionTestSupport.RunRepoInstaller(
                tempRoot,
                [
                    "-PackageArchivePath", archiveB,
                    "-InstallRoot", installRootB,
                    "-Client", "vscode",
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ]);

            installA.ExitCode.Should().Be(0, installA.Stderr);
            installB.ExitCode.Should().Be(0, installB.Stderr);

            var removalScriptPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1");
            if (useStandaloneFallback)
            {
                ReleaseScriptTestHarness.DeleteDirectory(
                    Path.Combine(installRootA, "x64", "current", "bin", "installer"));
                ReleaseScriptTestHarness.DeleteDirectory(
                    Path.Combine(installRootB, "x86", "current", "bin", "installer"));
                var standaloneRoot = Path.Combine(tempRoot, "standalone-removal");
                Directory.CreateDirectory(standaloneRoot);
                removalScriptPath = Path.Combine(standaloneRoot, "online-installer.ps1");
                File.Copy(
                    ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                    removalScriptPath,
                    overwrite: true);
                ReleaseScriptTestHarness.CopyOnlineInstallerRuntimeBundle(standaloneRoot);
            }

            var scopedRemoval = ReleaseScriptTestHarness.RunPowerShellScript(
                removalScriptPath,
                [
                    "-Action", "full-uninstall",
                    "-InstallRoot", installRootB + Path.DirectorySeparatorChar,
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ],
                StandaloneInstallerRegressionTestSupport.CreateStandaloneEnvironment(tempRoot));

            scopedRemoval.ExitCode.Should().Be(0, scopedRemoval.Stderr);
            using var removalJson = JsonDocument.Parse(scopedRemoval.Stdout);
            removalJson.RootElement.GetProperty("installRoot").GetString().Should().Be(installRootB);
            removalJson.RootElement.GetProperty("cleanupScope").GetString().Should()
                .Be("explicit-install-root-registrations-and-server-locations");
            removalJson.RootElement.GetProperty("cleanupGuidance").GetString().Should()
                .Contain("scoped to the exact -InstallRoot path");
            removalJson.RootElement.GetProperty("removedInstallRoots").EnumerateArray()
                .Select(root => root.GetString())
                .Should().Contain(installRootB).And.NotContain(installRootA);
            Directory.Exists(installRootB).Should().BeFalse();
            Directory.Exists(installRootA).Should().BeTrue();
            File.Exists(Path.Combine(installRootA, "x64", "client-registration", "other.mcpServers.json"))
                .Should().BeTrue("an explicit-root full-uninstall must not remove another root's registration artifact");

            var statePath = Path.Combine(tempRoot, "AppData", "Roaming", "WpfDevToolsMcp", "installer-state.json");
            using var stateJson = JsonDocument.Parse(File.ReadAllText(statePath));
            stateJson.RootElement.GetProperty("lastInstallRoot").GetString().Should().Be(installRootA);
            stateJson.RootElement.GetProperty("architectures").TryGetProperty("x64", out var x64State).Should().BeTrue();
            x64State.GetProperty("installRoot").GetString().Should().Be(installRootA);
            stateJson.RootElement.GetProperty("architectures").TryGetProperty("x86", out _).Should().BeFalse();
            var registrations = stateJson.RootElement.GetProperty("registrations").EnumerateObject().ToArray();
            registrations.Should().ContainSingle("root-A registration state must remain present");
            registrations[0].Value.GetProperty("installRoot").GetString().Should().Be(installRootA);

            var globalRemoval = ReleaseScriptTestHarness.RunPowerShellScript(
                removalScriptPath,
                [
                    "-Action", "full-uninstall",
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ],
                StandaloneInstallerRegressionTestSupport.CreateStandaloneEnvironment(tempRoot));

            globalRemoval.ExitCode.Should().Be(0, globalRemoval.Stderr);
            Directory.Exists(installRootA).Should().BeFalse(
                "omitting -InstallRoot must preserve the existing global full-uninstall behavior");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

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

    [Fact]
    public void SharedEmptyRootCleanup_BestEffort_ShouldContainEnumerationFailure()
    {
        var installRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var result = RunSharedEmptyRootCleanupProbe(
                installRoot,
                "function Get-ChildItem { param([string]$LiteralPath, [switch]$Force, $ErrorAction) throw 'simulated enumeration failure' }");

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stdout.Trim().Should().Be("0");
            Directory.Exists(installRoot).Should().BeTrue();
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(installRoot);
        }
    }

    [Fact]
    public void SharedEmptyRootCleanup_BestEffort_ShouldReportOnlyVerifiedRemoval()
    {
        var installRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var result = RunSharedEmptyRootCleanupProbe(
                installRoot,
                "function Remove-PathIfExists { param([string]$Path, [switch]$BestEffort) }");

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stdout.Trim().Should().Be("0");
            Directory.Exists(installRoot).Should().BeTrue();
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(installRoot);
        }
    }

    private static (int ExitCode, string Stdout, string Stderr) RunSharedEmptyRootCleanupProbe(
        string installRoot,
        string injectedFunction)
    {
        var command = string.Join(
            Environment.NewLine,
            [
                ". '" + ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Installer.Uninstall.Standalone.ps1").Replace("'", "''") + "'",
                "function Assert-InstallerLocalPathTrusted { param([string]$Path) return $Path }",
                injectedFunction,
                "$installation = [ordered]@{ InstallerOwned=$true; InstallRoot='" + installRoot.Replace("'", "''") + "' }",
                "@(Remove-InstallerOwnedEmptyInstallRoots -Installations @($installation) -BestEffort).Count"
            ]);

        return ReleaseScriptTestHarness.RunPowerShellCommand(command);
    }
}
