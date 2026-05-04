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
}
