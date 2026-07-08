using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class InstallerOnlineArchiveDownloadTests
{
    [Fact]
    public void OnlineInstallerScript_ShouldUseBoundedTimeoutForReleaseArchiveDownloads()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var command = string.Join(" ; ",
            [
                "$script:CapturedTimeoutSec = 0",
                OnlineInstallerScriptTestHarness.BuildDefinitionOnlyPrelude(
                    "-Action install -Version 1.2.3 -Architecture x64 -Client other -WorkingRoot '" +
                    tempRoot.Replace("'", "''") +
                    "' -NonInteractive"),
                "function Get-InstallerSharedModulePaths { return @() }",
                "function Resolve-AbsoluteDirectory { param([string]$Path) New-Item -ItemType Directory -Force -Path $Path | Out-Null; return $Path }",
                "function Test-PackageArchiveRequested { return $false }",
                "function Assert-ArchiveIntegrity { param([string]$ArchivePath, [string]$DownloadSource, [string]$ResolvedVersion, [string]$ResolvedArchitecture) return @{ DownloadUri = $null; PackageAssetName = 'release_1.2.3_win-x64.zip'; ResolvedVersion = $ResolvedVersion } }",
                "function Assert-ArchiveSafeEntries { param([string]$ArchivePath, [string]$DestinationPath) }",
                "function Resolve-LocalPackageRoot { throw 'not used' }",
                "function Resolve-PackageManifestPath { throw 'not used' }",
                "function Resolve-RequestedReleaseVersion { param([string]$RequestedVersion) return $RequestedVersion }",
                "function Get-ReleaseAssetDownloadDetails { param([string]$ResolvedVersion, [string]$ResolvedArchitecture) return @{ AssetName = 'release_1.2.3_win-x64.zip'; DownloadUri = 'https://example.invalid/release.zip'; ResolvedVersion = '1.2.3' } }",
                "function Invoke-WebRequest { param([string]$Uri, [string]$OutFile, [int]$TimeoutSec = 0) $script:CapturedTimeoutSec = $TimeoutSec; Set-Content -Path $OutFile -Value 'archive' -Encoding UTF8 }",
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

    [Fact]
    public void OnlineInstallerScript_ShouldFallbackToReleaseArchiveWhenRawHelperDownloadIsRateLimited()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var standaloneRoot = Path.Combine(tempRoot, "standalone");
            Directory.CreateDirectory(standaloneRoot);
            var scriptPath = Path.Combine(standaloneRoot, "online-installer.ps1");
            File.Copy(ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"), scriptPath);
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot, "x64", isolateArchiveContents: true);
            var helperSourceRoot = ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer");
            var workingRoot = Path.Combine(tempRoot, "working");

            var command = $$"""
{{OnlineInstallerScriptTestHarness.BuildDefinitionOnlyPrelude("-Action install -Version 1.2.3 -Architecture x64 -Client other -WorkingRoot '" + workingRoot.Replace("'", "''") + "' -NonInteractive", scriptPath)}}
$script:RequestedUris = New-Object System.Collections.Generic.List[string]
function Resolve-TuiHelperDownloadBaseUri { 'https://raw.example.invalid/helpers' }
function Get-TuiHelperArchiveDownloadDetails {
    return @{
        AssetName = 'release_1.2.3_win-x64.zip'
        DownloadUri = 'https://github.example.invalid/release_1.2.3_win-x64.zip'
        ResolvedVersion = '1.2.3'
    }
}
function Assert-TuiHelperArchiveIntegrity {
    param([string]$ArchivePath, $DownloadDetails)
}
function Invoke-WebRequest {
    param([string]$Uri, [string]$OutFile, $Headers, [int]$TimeoutSec)
    $script:RequestedUris.Add($Uri)
    $leafName = Split-Path ([System.Uri]$Uri).AbsolutePath -Leaf
    if ($leafName -eq 'installer-helpers.manifest.json') {
        Copy-Item -LiteralPath '{{helperSourceRoot.Replace("'", "''")}}\installer-helpers.manifest.json' -Destination $OutFile -Force
        return
    }

    if ($Uri -eq 'https://github.example.invalid/release_1.2.3_win-x64.zip') {
        Copy-Item -LiteralPath '{{archivePath.Replace("'", "''")}}' -Destination $OutFile -Force
        return
    }

    throw '429 Too Many Requests'
}

$helperRoot = Ensure-TuiHelpersAvailable -SuppressBootstrapOutput -RequiredHelperFiles (Get-InstallerSharedRuntimeHelperLeafNames)
[ordered]@{
    requestedUris = @($script:RequestedUris)
    helperRoot = $helperRoot
    actionsPathsAvailable = Test-Path -LiteralPath (Join-Path $helperRoot 'Installer.Actions.Paths.ps1')
} | ConvertTo-Json -Compress
""";

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            using var payload = JsonDocument.Parse(result.Stdout);
            payload.RootElement.GetProperty("actionsPathsAvailable").GetBoolean().Should().BeTrue();
            payload.RootElement.GetProperty("requestedUris").EnumerateArray()
                .Select(static uri => uri.GetString())
                .Should().Contain("https://github.example.invalid/release_1.2.3_win-x64.zip");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }
}
