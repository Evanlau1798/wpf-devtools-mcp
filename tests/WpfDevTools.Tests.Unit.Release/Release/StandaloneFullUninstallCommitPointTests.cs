using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class StandaloneFullUninstallCommitPointTests
{
    [Fact]
    public void StandaloneFullUninstall_ShouldKeepCommittedStateWhenSecondRollbackDisposalFails()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var installRoot = Path.Combine(tempRoot, "install-root");
            var x64Base = Path.Combine(installRoot, "x64");
            var x86Base = Path.Combine(installRoot, "x86");
            var statePath = Path.Combine(tempRoot, "installer-state.json");
            Directory.CreateDirectory(x64Base);
            Directory.CreateDirectory(x86Base);
            File.WriteAllText(Path.Combine(x64Base, "payload.txt"), "x64");
            File.WriteAllText(Path.Combine(x86Base, "payload.txt"), "x86");

            var command = string.Join(
                Environment.NewLine,
                [
                    ". '" + ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/OnlineInstaller.Runtime.07.ps1").Replace("'", "''") + "'",
                    "$script:testState = [ordered]@{ lastInstallRoot=$null; architectures=[ordered]@{}; registrations=[ordered]@{} }",
                    "$script:saveCompleted = $false; $script:disposalCount = 0",
                    "function Get-StandaloneInstallerState { return $script:testState }",
                    "function Resolve-StandaloneRemovalInstallRoot { return '" + installRoot.Replace("'", "''") + "' }",
                    "function Get-StandaloneDetectedInstallerInstallations { return @([ordered]@{ InstallRoot='" + installRoot.Replace("'", "''") + "'; Architecture='x64'; InstallBase='" + x64Base.Replace("'", "''") + "'; InstalledExecutable='x64.exe'; ResolvedVersion='test'; InstallerOwned=$true }, [ordered]@{ InstallRoot='" + installRoot.Replace("'", "''") + "'; Architecture='x86'; InstallBase='" + x86Base.Replace("'", "''") + "'; InstalledExecutable='x86.exe'; ResolvedVersion='test'; InstallerOwned=$true }) }",
                    "function Get-StandaloneDetectedInstallerRegistrations { return @() }",
                    "function Get-StandaloneManagedRegistrationsFromInstall { return @() }",
                    "function Resolve-StandaloneInstallerStatePath { return '" + statePath.Replace("'", "''") + "' }",
                    "function Assert-InstallerLocalPathTrusted { param([string]$Path) return $Path }",
                    "function Move-StandalonePathWithRetry { param([string]$SourcePath, [string]$DestinationPath) Move-Item -LiteralPath $SourcePath -Destination $DestinationPath }",
                    "function Remove-StandaloneRuntimeScreenshotCache { return $false }",
                    "function Save-StandaloneInstallerState { param($State) $script:saveCompleted=$true; '{\"lastInstallRoot\":null,\"architectures\":{},\"registrations\":{}}' | Set-Content -LiteralPath '" + statePath.Replace("'", "''") + "'; return '" + statePath.Replace("'", "''") + "' }",
                    "function Remove-PathIfExists { param([string]$Path, [switch]$BestEffort) if ([string]::IsNullOrWhiteSpace($Path)) { return }; if ($script:saveCompleted -and $Path -like '*.rollback-*') { $script:disposalCount++; if ($script:disposalCount -eq 2) { if ($BestEffort) { return }; throw 'simulated second rollback disposal failure' } }; if (Test-Path -LiteralPath $Path) { Remove-Item -LiteralPath $Path -Recurse -Force } }",
                    "function Remove-StandaloneInstallerOwnedEmptyInstallRoots { return @() }",
                    "function Get-StandaloneFullUninstallResultSummary { return [ordered]@{ version='test'; resolvedVersion='test'; installRoot=$null; releaseChannel='test' } }",
                    "function Get-StandaloneFullUninstallCleanupGuidance { return 'test guidance' }",
                    "$succeeded=$false; try { Invoke-StandaloneFullUninstallActionCore -ResolvedAction full-uninstall -ResolvedArchitecture x64 -ResolvedClient all -ResolvedInstallRoot '' -RequestedVersion test | Out-Null; $succeeded=$true } catch { }",
                    "$rollbackResidue = @(Get-ChildItem -LiteralPath '" + installRoot.Replace("'", "''") + "' -Filter '*.rollback-*' -Force -ErrorAction SilentlyContinue).Count",
                    "[ordered]@{ Succeeded=$succeeded; StateExists=(Test-Path -LiteralPath '" + statePath.Replace("'", "''") + "'); X64Exists=(Test-Path -LiteralPath '" + x64Base.Replace("'", "''") + "'); X86Exists=(Test-Path -LiteralPath '" + x86Base.Replace("'", "''") + "'); RollbackResidueCount=$rollbackResidue } | ConvertTo-Json -Compress"
                ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            using var json = JsonDocument.Parse(result.Stdout);
            json.RootElement.GetProperty("Succeeded").GetBoolean().Should().BeTrue();
            json.RootElement.GetProperty("StateExists").GetBoolean().Should().BeTrue();
            json.RootElement.GetProperty("X64Exists").GetBoolean().Should().BeFalse();
            json.RootElement.GetProperty("X86Exists").GetBoolean().Should().BeFalse();
            json.RootElement.GetProperty("RollbackResidueCount").GetInt32().Should().Be(1);
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }
}
