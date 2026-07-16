using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class StandaloneFullUninstallResilienceTests
{
    [Fact]
    public void StandaloneFullUninstall_ShouldCommitWhenEmptyRootCleanupFails()
    {
        var statePath = Path.Combine(
            ReleaseScriptTestHarness.CreateTempDirectory(),
            "installer-state.json");
        try
        {
            var command = string.Join(
                Environment.NewLine,
                [
                    ". '" + ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/OnlineInstaller.Runtime.07.ps1").Replace("'", "''") + "'",
                    "$script:testState = [ordered]@{ lastInstallRoot=$null; architectures=[ordered]@{}; registrations=[ordered]@{} }",
                    "function Get-StandaloneInstallerState { return $script:testState }",
                    "function Resolve-StandaloneRemovalInstallRoot { return 'C:\\test-root' }",
                    "function Get-StandaloneDetectedInstallerInstallations { return @() }",
                    "function Get-StandaloneDetectedInstallerRegistrations { return @() }",
                    "function Resolve-StandaloneInstallerStatePath { return '" + statePath.Replace("'", "''") + "' }",
                    "function Save-StandaloneInstallerState { param($State) return '" + statePath.Replace("'", "''") + "' }",
                    "function Remove-PathIfExists { param([string]$Path) }",
                    "function Remove-StandaloneInstallerOwnedEmptyInstallRoots { param([object[]]$Installations, [switch]$BestEffort) if (-not $BestEffort) { throw 'simulated empty-root cleanup failure' }; return @() }",
                    "function Get-StandaloneFullUninstallResultSummary { return [ordered]@{ version='test'; resolvedVersion='test'; installRoot=$null; releaseChannel='test' } }",
                    "function Get-StandaloneFullUninstallCleanupGuidance { return 'test guidance' }",
                    "Invoke-StandaloneFullUninstallActionCore -ResolvedAction full-uninstall -ResolvedArchitecture x64 -ResolvedClient all -ResolvedInstallRoot '' -RequestedVersion test | ConvertTo-Json -Compress"
                ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stdout.Should().Contain("\"action\":\"full-uninstall\"");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(Path.GetDirectoryName(statePath)!);
        }
    }

    [Fact]
    public void StandaloneFullUninstall_WithoutState_ShouldDiscoverDefaultInstallRoot()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var environment = StandaloneInstallerRegressionTestSupport.CreateStandaloneEnvironment(tempRoot);
            var defaultInstallRoot = Path.Combine(environment["APPDATA"]!, "WpfDevToolsMcp");

            var install = StandaloneInstallerRegressionTestSupport.RunRepoInstaller(
                tempRoot,
                [
                    "-PackageArchivePath", archivePath,
                    "-Client", "other",
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ],
                environment);

            install.ExitCode.Should().Be(0, install.Stderr);
            Directory.Exists(defaultInstallRoot).Should().BeTrue();

            File.Delete(Path.Combine(environment["APPDATA"]!, "WpfDevToolsMcp", "installer-state.json"));
            ReleaseScriptTestHarness.DeleteDirectory(
                Path.Combine(defaultInstallRoot, "x64", "current", "bin", "installer"));
            var standaloneInstaller = CreateStandaloneInstaller(tempRoot);

            var removal = ReleaseScriptTestHarness.RunPowerShellScript(
                standaloneInstaller,
                [
                    "-Action", "full-uninstall",
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ],
                environment);

            removal.ExitCode.Should().Be(0, removal.Stderr);
            using var removalJson = JsonDocument.Parse(removal.Stdout);
            removalJson.RootElement.GetProperty("removedInstallations").GetArrayLength().Should().Be(1,
                "global standalone cleanup must probe the effective default root even without persisted state");
            Directory.Exists(Path.Combine(defaultInstallRoot, "x64")).Should().BeFalse();
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    private static string CreateStandaloneInstaller(string tempRoot)
    {
        var standaloneRoot = Path.Combine(tempRoot, "standalone-removal");
        Directory.CreateDirectory(standaloneRoot);
        var standaloneInstaller = Path.Combine(standaloneRoot, "online-installer.ps1");
        File.Copy(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
            standaloneInstaller,
            overwrite: true);
        ReleaseScriptTestHarness.CopyOnlineInstallerRuntimeBundle(standaloneRoot);
        return standaloneInstaller;
    }
}
