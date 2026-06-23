using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class InstallerPrereleaseDownloadMetadataTests
{
    [Fact]
    public void InvokeInstallerActionCore_Install_ShouldReportActualPrereleaseDownloadUri()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var packageDirectory = Path.Combine(tempRoot, "package");
            var packageManifestPath = Path.Combine(packageDirectory, "manifest.json");
            var installRoot = Path.Combine(tempRoot, "install-root");
            var installBase = Path.Combine(installRoot, "x64");
            var installedExecutable = Path.Combine(installBase, "current", "bin", "wpf-devtools-x64.exe");
            var statePath = Path.Combine(tempRoot, "installer-state.json");
            var registrationPath = Path.Combine(installBase, "client-registration", "other.mcpServers.json");
            const string prereleaseVersion = "0.1.0-e2e.20260623132038";
            const string packageVersion = "0.1.0";
            var prereleaseAssetName = $"release_{prereleaseVersion}_win-x64.zip";
            var prereleaseDownloadUri =
                $"https://github.com/Evanlau1798/wpf-devtools-mcp/releases/download/v{prereleaseVersion}/{prereleaseAssetName}";

            Directory.CreateDirectory(packageDirectory);
            File.WriteAllText(
                packageManifestPath,
                JsonSerializer.Serialize(new
                {
                    architecture = "x64",
                    version = packageVersion
                }));

            var command = string.Join(
                Environment.NewLine,
                [
                    ". '" + ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Installer.Actions.ps1").Replace("'", "''") + "'",
                    "$global:NonInteractive = $true",
                    "$global:OutputJson = $true",
                    "function Resolve-InstallerMode { return 'online' }",
                    "function Resolve-PackageSession { param([string]$Mode, [string]$ResolvedVersion, [string]$ResolvedArchitecture) return [ordered]@{ PackageDirectory='" + packageDirectory.Replace("'", "''") + "'; ResolvedVersion='" + prereleaseVersion + "'; PackageAssetName='" + prereleaseAssetName + "'; DownloadSource='github-release'; DownloadUri='" + prereleaseDownloadUri + "'; CleanupSession=$false; SessionRoot='" + Path.Combine(tempRoot, "session").Replace("'", "''") + "' } }",
                    "function Resolve-PackageManifestPath { param([string]$PackageDirectory) return '" + packageManifestPath.Replace("'", "''") + "' }",
                    "function Get-ReleaseAssetIdentity { param([string]$AssetName) return [ordered]@{ AssetName = $AssetName; ResolvedVersion = '" + prereleaseVersion + "'; ResolvedArchitecture = 'x64' } }",
                    "function Get-ReleaseDownloadUri { param([string]$ResolvedVersion, [string]$ResolvedArchitecture) return 'https://computed.invalid/releases/download/v' + $ResolvedVersion + '/release_' + $ResolvedVersion + '_win-' + $ResolvedArchitecture + '.zip' }",
                    "function Install-PackagePayload { param([string]$PackageDirectory, $PackageManifest, [string]$ResolvedArchitecture, [string]$ResolvedInstallRoot, [string]$ResolvedVersion) return [ordered]@{ installRoot='" + installRoot.Replace("'", "''") + "'; installBase='" + installBase.Replace("'", "''") + "'; installedExecutable='" + installedExecutable.Replace("'", "''") + "'; reusedExistingBinary=$false } }",
                    "function Invoke-ClientRegistration { param([string]$SelectedClient, [string]$InstalledExecutable, [string]$InstallBase) return @([ordered]@{ client='other'; mode='artifact-only'; target='" + registrationPath.Replace("'", "''") + "'; backupPath=$null; applied=$true }) }",
                    "function Invoke-InstallVerification { param([string]$SelectedClient, [string]$ResolvedVersion, [string]$InstalledExecutable, $Registration) return [ordered]@{ Succeeded = $true; InstalledVersion = '" + packageVersion + "'; VerificationMessage = 'ok'; LastVerifiedUtc = '2026-06-23T00:00:00.0000000Z' } }",
                    "function Update-InstalledManifestManagedRegistrationTarget { param([string]$InstallBase, [string]$SelectedClient, $Registration) }",
                    "function Get-InstallerState { return [ordered]@{ lastInstallRoot=$null; architectures=[ordered]@{}; registrations=[ordered]@{} } }",
                    "function Resolve-ClientStateKey { param([string]$ClientId, [string]$RegistrationMode) return $ClientId }",
                    "function Update-InstallerStateAfterInstall { param($State, [string]$ResolvedInstallRoot, [string]$ResolvedArchitecture, [string]$ResolvedVersion, [string]$InstalledExecutable, [string]$SelectedClient, $Registration, [string]$LastVerifiedUtc) }",
                    "function Save-InstallerState { param($State) return '" + statePath.Replace("'", "''") + "' }",
                    "function Complete-InstalledPayloadCommit { param($InstallResult) }",
                    "function Remove-PathIfExists { param([string]$Path) }",
                    "$result = Invoke-InstallerActionCore -ResolvedAction 'install' -ResolvedArchitecture 'x64' -ResolvedClient 'other' -ResolvedInstallRoot '" + installRoot.Replace("'", "''") + "' -RequestedVersion 'v" + prereleaseVersion + "'",
                    "$result | ConvertTo-Json -Compress -Depth 8"
                ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            using var json = JsonDocument.Parse(result.Stdout);
            json.RootElement.GetProperty("resolvedVersion").GetString().Should().Be(packageVersion);
            json.RootElement.GetProperty("packageAssetName").GetString().Should().Be(prereleaseAssetName);
            json.RootElement.GetProperty("downloadUri").GetString().Should().Be(prereleaseDownloadUri);
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }
}
