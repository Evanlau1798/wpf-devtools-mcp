using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class InstallerArchiveExtractionProgressTests
{
    [Fact]
    public void ResolvePackageSession_LocalArchiveExtraction_ShouldSuppressProgressAndRestorePreference()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = Path.Combine(tempRoot, "release_1.2.3_win-x64.zip");
            File.WriteAllText(archivePath, "archive");

            var command = string.Join(" ; ",
            [
                OnlineInstallerScriptTestHarness.BuildDefinitionOnlyPrelude(
                    "-Action install -Version 1.2.3 -Architecture x64 -Client other -WorkingRoot '" +
                    tempRoot.Replace("'", "''") +
                    "' -PackageArchivePath '" +
                    archivePath.Replace("'", "''") +
                    "' -NonInteractive"),
                "function Get-InstallerSharedModulePaths { return @() }",
                "function Resolve-AbsoluteDirectory { param([string]$Path) New-Item -ItemType Directory -Force -Path $Path | Out-Null; return $Path }",
                "function Assert-ArchiveIntegrity { param([string]$ArchivePath, [string]$DownloadSource, [string]$ResolvedVersion, [string]$ResolvedArchitecture) return @{ DownloadUri = 'local'; PackageAssetName = 'release_1.2.3_win-x64.zip'; ResolvedVersion = $ResolvedVersion } }",
                "function Assert-ArchiveSafeEntries { param([string]$ArchivePath, [string]$DestinationPath) }",
                "function Expand-Archive { param([string]$Path, [string]$DestinationPath, [switch]$Force) if ($ProgressPreference -ne 'SilentlyContinue') { throw ('progress host failed: ' + $ProgressPreference) }; New-Item -ItemType Directory -Force -Path $DestinationPath | Out-Null }",
                "$ProgressPreference = 'Continue'",
                "$null = Resolve-PackageSession -Mode offline -ResolvedVersion '1.2.3' -ResolvedArchitecture 'x64'",
                "if ($ProgressPreference -ne 'Continue') { throw ('ProgressPreference was not restored: ' + $ProgressPreference) }"
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void ResolvePackageSession_OnlineArchiveExtraction_ShouldSuppressProgressAndRestorePreference()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var command = string.Join(" ; ",
            [
                OnlineInstallerScriptTestHarness.BuildDefinitionOnlyPrelude(
                    "-Action install -Version 1.2.3 -Architecture x64 -Client other -WorkingRoot '" +
                    tempRoot.Replace("'", "''") +
                    "' -NonInteractive"),
                "function Get-InstallerSharedModulePaths { return @() }",
                "function Resolve-AbsoluteDirectory { param([string]$Path) New-Item -ItemType Directory -Force -Path $Path | Out-Null; return $Path }",
                "function Assert-ArchiveIntegrity { param([string]$ArchivePath, [string]$DownloadSource, [string]$ResolvedVersion, [string]$ResolvedArchitecture) return @{ DownloadUri = 'github'; PackageAssetName = 'release_1.2.3_win-x64.zip'; ResolvedVersion = $ResolvedVersion } }",
                "function Assert-ArchiveSafeEntries { param([string]$ArchivePath, [string]$DestinationPath) }",
                "function Resolve-RequestedReleaseVersion { param([string]$RequestedVersion) return $RequestedVersion }",
                "function Get-ReleaseAssetDownloadDetails { param([string]$ResolvedVersion, [string]$ResolvedArchitecture) return @{ AssetName = 'release_1.2.3_win-x64.zip'; DownloadUri = 'https://example.invalid/release.zip'; ResolvedVersion = '1.2.3' } }",
                "function Invoke-WebRequest { param([string]$Uri, [string]$OutFile, [int]$TimeoutSec = 0) Set-Content -Path $OutFile -Value 'archive' -Encoding UTF8 }",
                "function Expand-Archive { param([string]$Path, [string]$DestinationPath, [switch]$Force) if ($ProgressPreference -ne 'SilentlyContinue') { throw ('progress host failed: ' + $ProgressPreference) }; New-Item -ItemType Directory -Force -Path $DestinationPath | Out-Null }",
                "$ProgressPreference = 'Continue'",
                "$null = Resolve-PackageSession -Mode online -ResolvedVersion '1.2.3' -ResolvedArchitecture 'x64'",
                "if ($ProgressPreference -ne 'Continue') { throw ('ProgressPreference was not restored: ' + $ProgressPreference) }"
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }
}
