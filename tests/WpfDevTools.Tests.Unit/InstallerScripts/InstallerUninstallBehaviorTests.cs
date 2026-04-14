using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class InstallerUninstallBehaviorTests
{
    [Fact]
    public void OnlineInstallerScript_ShouldDeclareSharedDiscoveryAndUninstallHelpers()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));

        content.Should().Contain("scripts/installer/Installer.Discovery.ps1");
        content.Should().Contain("scripts/installer/Installer.Uninstall.ps1");
        content.Should().Contain("scripts/installer/Tui.Confirm.ps1");
    }

    [Fact]
    public void OnlineInstallerScript_ShouldDeclareHelperCacheKeyAndVerifiedRemovalContracts()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));

        content.Should().Contain("installer-helpers.manifest.json");
        content.Should().Contain("Get-InstallerHelperRuntimeCacheKey");
        content.Should().Contain("helper-cache-key.txt");
        content.Should().Contain("Remove-PathIfExists -Path $runtimeRoot");
        content.Should().Contain("InstallerOwned");
        content.Should().Contain("RegistrationMode");
        content.Should().Contain("InstalledExecutable");
    }

    [Fact]
    public void OnlineInstallerScript_ShouldDeclareDualUninstallModes()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));

        content.Should().Contain("unregister");
        content.Should().Contain("full-uninstall");
        content.Should().Contain("Full Uninstall");
    }

    [Fact]
    public void InstallerDiscoveryMerge_ShouldPreferExternalEvidenceForMutableInstallationFields()
    {
        var discoveryScriptPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Installer.Discovery.ps1");
        var command = string.Join(" ; ",
        [
            ". '" + discoveryScriptPath.Replace("'", "''") + "'",
            "$primary = [ordered]@{ ClientId='vscode'; RegistrationMode='state'; RegistrationTarget='state.json'; InstalledExecutable='C:\\old-root\\wpf-devtools-x64.exe'; InstallRoot='C:\\old-root'; Architecture='x64'; InstallerOwned=$true; EvidenceSource='state'; ResolvedVersion='1.0.0'; LastVerifiedUtc='2026-03-30T00:00:00Z' }",
            "$secondary = [ordered]@{ ClientId='vscode'; RegistrationMode='json-file'; RegistrationTarget='C:\\config\\mcp.json'; InstalledExecutable='C:\\new-root\\wpf-devtools-arm64.exe'; InstallRoot='C:\\new-root'; Architecture='arm64'; InstallerOwned=$true; EvidenceSource='json-file'; ResolvedVersion='1.2.3'; LastVerifiedUtc=$null }",
            "$merged = Merge-DetectedInstallerRegistration -Primary $primary -Secondary $secondary",
            "$merged | ConvertTo-Json -Compress"
        ]);

        var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

        result.ExitCode.Should().Be(0, result.Stderr);
        using var json = JsonDocument.Parse(result.Stdout);
        var root = json.RootElement;
        root.GetProperty("RegistrationMode").GetString().Should().Be("json-file");
        root.GetProperty("RegistrationTarget").GetString().Should().Be(@"C:\config\mcp.json");
        root.GetProperty("InstalledExecutable").GetString().Should().Be(@"C:\new-root\wpf-devtools-arm64.exe");
        root.GetProperty("InstallRoot").GetString().Should().Be(@"C:\new-root");
        root.GetProperty("Architecture").GetString().Should().Be("arm64");
        root.GetProperty("EvidenceSource").GetString().Should().Be("json-file");
        root.GetProperty("ResolvedVersion").GetString().Should().Be("1.2.3");
        root.GetProperty("LastVerifiedUtc").GetString().Should().Be("2026-03-30T00:00:00Z");
    }

    [Fact]
    public void RemovePathIfExists_ShouldTreatWildcardCharactersAsLiteralPathSegments()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var targetPath = Path.Combine(tempRoot, "target[1]");
            var siblingPath = Path.Combine(tempRoot, "target1");
            Directory.CreateDirectory(targetPath);
            Directory.CreateDirectory(siblingPath);

            var command = string.Join(" ; ",
            [
                "$repoScriptPath='" + ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1").Replace("'", "''") + "'",
                "Set-Location '" + tempRoot.Replace("'", "''") + "'",
                ". ([scriptblock]::Create((Get-Content $repoScriptPath -Raw))) -Action uninstall -Architecture x64 -Client other -NonInteractive -Force -OutputJson | Out-Null",
                "Remove-PathIfExists -Path '" + targetPath.Replace("'", "''") + "'",
                "@{ TargetExists = (Test-Path -LiteralPath '" + targetPath.Replace("'", "''") + "'); SiblingExists = (Test-Path -LiteralPath '" + siblingPath.Replace("'", "''") + "') } | ConvertTo-Json -Compress"
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(
                command,
                new Dictionary<string, string?>
                {
                    ["APPDATA"] = Path.Combine(tempRoot, "AppData", "Roaming"),
                    ["LOCALAPPDATA"] = Path.Combine(tempRoot, "AppData", "Local"),
                    ["USERPROFILE"] = Path.Combine(tempRoot, "UserProfile"),
                    ["WPFDEVTOOLS_INSTALLER_HELPER_DIRECTORY"] = ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer")
                });

            result.ExitCode.Should().Be(0, result.Stderr);
            using var json = JsonDocument.Parse(result.Stdout);
            json.RootElement.GetProperty("TargetExists").GetBoolean().Should().BeFalse();
            json.RootElement.GetProperty("SiblingExists").GetBoolean().Should().BeTrue();
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void InstallerReleaseModule_ShouldUseBoundedTimeoutForOnlineArchiveDownloads()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var command = string.Join(" ; ",
            [
                "$script:CapturedTimeoutSec = 0",
                "$script:WorkingRoot = '" + tempRoot.Replace("'", "''") + "'",
                ". '" + ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Installer.Release.ps1").Replace("'", "''") + "'",
                "function Resolve-AbsoluteDirectory { param([string]$Path) New-Item -ItemType Directory -Force -Path $Path | Out-Null; return $Path }",
                "function Test-PackageArchiveRequested { return $false }",
                "function Assert-ArchiveIntegrity { param([string]$ArchivePath, [string]$DownloadSource, [string]$ResolvedVersion, [string]$ResolvedArchitecture) return @{ DownloadUri = $null; PackageAssetName = 'release_1.2.3_win-x64.zip'; ResolvedVersion = $ResolvedVersion } }",
                "function Resolve-LocalPackageRoot { throw 'not used' }",
                "function Resolve-PackageManifestPath { throw 'not used' }",
                "function Resolve-RequestedReleaseVersion { param([string]$RequestedVersion) return $RequestedVersion }",
                "function Get-ReleaseAssetDownloadDetails { param([string]$ResolvedVersion, [string]$ResolvedArchitecture) return @{ AssetName = 'release_1.2.3_win-x64.zip'; DownloadUri = 'https://example.invalid/release.zip'; ResolvedVersion = '1.2.3' } }",
                "function Invoke-WebRequest { param([string]$Uri, [string]$OutFile, [int]$TimeoutSec) $script:CapturedTimeoutSec = $TimeoutSec; Set-Content -Path $OutFile -Value 'archive' -Encoding UTF8 }",
                "function Expand-Archive { param([string]$Path, [string]$DestinationPath, [switch]$Force) New-Item -ItemType Directory -Force -Path $DestinationPath | Out-Null }",
                "$null = Resolve-PackageSession -Mode online -ResolvedVersion '1.2.3' -ResolvedArchitecture 'x64'",
                "$script:CapturedTimeoutSec"
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            int.Parse(result.Stdout.Trim()).Should().BeGreaterThan(0);
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }
}
