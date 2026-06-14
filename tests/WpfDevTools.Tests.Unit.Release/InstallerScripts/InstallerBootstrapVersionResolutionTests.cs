using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Tests.Unit;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed partial class InstallerBootstrapTests
{
    [Fact]
    public void StartLatestInstallerVersionRefresh_ShouldEscapeSingleQuotedPathsAndUrisInBackgroundCommand()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var quotedTempRoot = Path.Combine(tempRoot, "user's-temp");
            Directory.CreateDirectory(quotedTempRoot);
            var command = $$"""
$env:APPDATA='{{Path.Combine(tempRoot, "AppData", "Roaming").Replace("'", "''")}}'
$env:LOCALAPPDATA='{{Path.Combine(tempRoot, "AppData", "Local").Replace("'", "''")}}'
$env:USERPROFILE='{{Path.Combine(tempRoot, "UserProfile").Replace("'", "''")}}'
$env:TEMP='{{quotedTempRoot.Replace("'", "''")}}'
{{OnlineInstallerScriptTestHarness.BuildDefinitionOnlyPrelude("-Action install -Version latest -Architecture x64 -Client other -InstallRoot '" + Path.Combine(tempRoot, "install-root").Replace("'", "''") + "' -WorkingRoot '" + Path.Combine(tempRoot, "working").Replace("'", "''") + "' -NonInteractive")}}
function Get-GitHubReleaseApiUri { param([string]$ResolvedVersion) return 'https://example.invalid/releases/o''clock' }
function ConvertTo-PowerShellEncodedCommand {
    param([string]$CommandText)
    $script:capturedCommandText = $CommandText
    return 'V3JpdGUtT3V0cHV0ICd0ZXN0Jw=='
}
$handle = Start-LatestInstallerVersionRefresh
try {
    [string]$script:capturedCommandText
}
finally {
    Stop-LatestInstallerVersionRefresh -RefreshHandle $handle
}
""";

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stdout.Should().Contain("https://example.invalid/releases/o''clock");
            result.Stdout.Should().NotContain("Set-Content");
            result.Stdout.Should().NotContain("user''s-temp");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstallerScript_InlineIexExecution_WhenBootstrapFails_ShouldNotCreatePersistentInstallerStateFromReadOnlyLatestLookup()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var appData = Path.Combine(tempRoot, "AppData", "Roaming");
            var localAppData = Path.Combine(tempRoot, "AppData", "Local");
            var userProfile = Path.Combine(tempRoot, "UserProfile");
            Directory.CreateDirectory(appData);
            Directory.CreateDirectory(localAppData);
            Directory.CreateDirectory(userProfile);

            var command = $$"""
$env:APPDATA='{{appData.Replace("'", "''")}}'
$env:LOCALAPPDATA='{{localAppData.Replace("'", "''")}}'
$env:USERPROFILE='{{userProfile.Replace("'", "''")}}'
{{OnlineInstallerScriptTestHarness.BuildDefinitionOnlyPrelude("-Action install -Version latest -Architecture x64 -Client other -InstallRoot '" + Path.Combine(tempRoot, "install-root").Replace("'", "''") + "' -WorkingRoot '" + Path.Combine(tempRoot, "working").Replace("'", "''") + "' -NonInteractive")}}
$null = Get-LatestInstallerVersion -UseCacheOnly
[ordered]@{
    stateRootExists = Test-Path (Join-Path $env:APPDATA 'WpfDevToolsMcp')
} | ConvertTo-Json -Depth 3
""";

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            using var payload = JsonDocument.Parse(result.Stdout);
            payload.RootElement.GetProperty("stateRootExists").GetBoolean().Should().BeFalse(
                "read-only latest-version hints must not leave persistent installer state behind");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstallerScript_FunctionDefinitions_ShouldReuseSingleResolvedLatestVersionForHelperArchiveAndPackagePayload()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var appData = Path.Combine(tempRoot, "AppData", "Roaming");
            var localAppData = Path.Combine(tempRoot, "AppData", "Local");
            var userProfile = Path.Combine(tempRoot, "UserProfile");
            var workingRoot = Path.Combine(tempRoot, "working");
            Directory.CreateDirectory(appData);
            Directory.CreateDirectory(localAppData);
            Directory.CreateDirectory(userProfile);
            Directory.CreateDirectory(workingRoot);

            var command = $$"""
$env:APPDATA='{{appData.Replace("'", "''")}}'
$env:LOCALAPPDATA='{{localAppData.Replace("'", "''")}}'
$env:USERPROFILE='{{userProfile.Replace("'", "''")}}'
$env:TEMP='{{tempRoot.Replace("'", "''")}}'
$env:WPFDEVTOOLS_INSTALLER_HELPER_DIRECTORY='{{ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer").Replace("'", "''")}}'
{{OnlineInstallerScriptTestHarness.BuildDefinitionOnlyPrelude("-Action install -Version latest -Architecture x64 -Client other -InstallRoot '" + Path.Combine(tempRoot, "install-root").Replace("'", "''") + "' -WorkingRoot '" + workingRoot.Replace("'", "''") + "' -NonInteractive")}}
$global:latestCallCount = 0
$script:TestArchiveBytes = $null
$script:TestArchiveHash = $null
function Get-TestArchiveBytes {
    if ($null -ne $script:TestArchiveBytes) {
        return $script:TestArchiveBytes
    }

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $memoryStream = New-Object System.IO.MemoryStream
    try {
        $archive = New-Object System.IO.Compression.ZipArchive($memoryStream, [System.IO.Compression.ZipArchiveMode]::Create, $true)
        try {
            $entry = $archive.CreateEntry('bin/placeholder.txt')
            $entry.LastWriteTime = [DateTimeOffset]'2024-01-01T00:00:00Z'
            $writer = New-Object System.IO.StreamWriter($entry.Open())
            try {
                $writer.Write('stub')
            }
            finally {
                $writer.Dispose()
            }
        }
        finally {
            $archive.Dispose()
        }

        $script:TestArchiveBytes = $memoryStream.ToArray()
    }
    finally {
        $memoryStream.Dispose()
    }

    return $script:TestArchiveBytes
}
function Get-TestArchiveHash {
    if ($null -ne $script:TestArchiveHash) {
        return $script:TestArchiveHash
    }

    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        $script:TestArchiveHash = (($sha256.ComputeHash((Get-TestArchiveBytes)) | ForEach-Object { $_.ToString('x2') }) -join '')
    }
    finally {
        $sha256.Dispose()
    }

    return $script:TestArchiveHash
}
function Invoke-RestMethod {
    param([string]$Uri, $Headers, [int]$TimeoutSec)
    if ($Uri -like '*/releases/latest') {
        $global:latestCallCount += 1
        if ($global:latestCallCount -eq 1) {
            return [pscustomobject]@{ tag_name = 'v1.2.3'; assets = @() }
        }

        return [pscustomobject]@{
            tag_name = 'v9.9.9'
            assets = @(
                [pscustomobject]@{
                    name = 'release_latest_win-x64.zip'
                    browser_download_url = 'https://example.invalid/release_latest_win-x64.zip'
                }
            )
        }
    }

    if ($Uri -like '*/tags/v1.2.3') {
        return [pscustomobject]@{
            tag_name = 'v1.2.3'
            assets = @(
                [pscustomobject]@{
                    name = 'release_1.2.3_win-x64.zip'
                    browser_download_url = 'https://example.invalid/release_1.2.3_win-x64.zip'
                },
                [pscustomobject]@{
                    name = 'release-assets.json'
                    browser_download_url = 'https://example.invalid/release-assets.json'
                }
            )
        }
    }

    if ($Uri -eq 'https://example.invalid/release-assets.json') {
        return [pscustomobject]@{
            assets = @(
                [pscustomobject]@{
                    name = 'release_1.2.3_win-x64.zip'
                    sha256 = (Get-TestArchiveHash)
                }
            )
        }
    }

    throw "Unexpected Invoke-RestMethod URI: $Uri"
}
function Invoke-WebRequest {
    param([string]$Uri, [string]$OutFile, $Headers, [int]$TimeoutSec)
    [System.IO.File]::WriteAllBytes($OutFile, (Get-TestArchiveBytes))
}
Set-Item -Path function:Assert-ArchiveSafeEntries -Value {
    param([string]$ArchivePath, [string]$DestinationPath)
}
function Expand-Archive {
    param([string]$Path, [string]$DestinationPath, [switch]$Force)
    $binDir = Join-Path $DestinationPath 'bin'
    New-Item -ItemType Directory -Force -Path $binDir | Out-Null
    Set-Content -Path (Join-Path $binDir 'manifest.json') -Value '{"version":"1.2.3"}' -Encoding UTF8
    Set-Content -Path (Join-Path $binDir 'wpf-devtools-x64.exe') -Value 'stub' -Encoding UTF8
}
$helperDownload = Get-TuiHelperArchiveDownloadDetails
$session = Resolve-PackageSession -Mode online -ResolvedVersion latest -ResolvedArchitecture x64
[ordered]@{
    helperDownloadUri = [string]$helperDownload.DownloadUri
    helperResolvedVersion = [string]$helperDownload.ResolvedVersion
    packageResolvedVersion = [string]$session.ResolvedVersion
} | ConvertTo-Json -Depth 3
""";

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            using var payload = JsonDocument.Parse(result.Stdout);
            payload.RootElement.GetProperty("helperDownloadUri").GetString().Should().Contain("release_1.2.3_win-x64.zip");
            payload.RootElement.GetProperty("helperResolvedVersion").GetString().Should().Be("1.2.3");
            payload.RootElement.GetProperty("packageResolvedVersion").GetString().Should().Be("1.2.3");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstallerScript_FunctionDefinitions_WithPrereleaseLatest_ShouldResolvePrereleaseTagForHelperArchiveAndPackagePayload()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var appData = Path.Combine(tempRoot, "AppData", "Roaming");
            var localAppData = Path.Combine(tempRoot, "AppData", "Local");
            var userProfile = Path.Combine(tempRoot, "UserProfile");
            var workingRoot = Path.Combine(tempRoot, "working");
            Directory.CreateDirectory(appData);
            Directory.CreateDirectory(localAppData);
            Directory.CreateDirectory(userProfile);
            Directory.CreateDirectory(workingRoot);

            var command = $$"""
$env:APPDATA='{{appData.Replace("'", "''")}}'
$env:LOCALAPPDATA='{{localAppData.Replace("'", "''")}}'
$env:USERPROFILE='{{userProfile.Replace("'", "''")}}'
$env:TEMP='{{tempRoot.Replace("'", "''")}}'
$env:WPFDEVTOOLS_INSTALLER_HELPER_DIRECTORY='{{ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer").Replace("'", "''")}}'
{{OnlineInstallerScriptTestHarness.BuildDefinitionOnlyPrelude("-Action install -Version latest -Prerelease -Architecture x64 -Client other -InstallRoot '" + Path.Combine(tempRoot, "install-root").Replace("'", "''") + "' -WorkingRoot '" + workingRoot.Replace("'", "''") + "' -NonInteractive")}}
function Invoke-RestMethod {
    param([string]$Uri, $Headers, [int]$TimeoutSec)
    if ($Uri -like '*/releases?per_page=20') {
        return @(
            [pscustomobject]@{ tag_name = 'v9.9.9'; prerelease = $false; draft = $false; assets = @() },
            [pscustomobject]@{ tag_name = 'v2.0.0-preview.1'; prerelease = $true; draft = $false; assets = @() },
            [pscustomobject]@{ tag_name = 'v2.0.0-preview.2'; prerelease = $true; draft = $true; assets = @() }
        )
    }

    if ($Uri -like '*/tags/v2.0.0-preview.1') {
        return [pscustomobject]@{
            tag_name = 'v2.0.0-preview.1'
            assets = @(
                [pscustomobject]@{
                    name = 'release_2.0.0-preview.1_win-x64.zip'
                    browser_download_url = 'https://example.invalid/release_2.0.0-preview.1_win-x64.zip'
                }
            )
        }
    }

    throw "Unexpected Invoke-RestMethod URI: $Uri"
}
$helperDownload = Get-TuiHelperArchiveDownloadDetails
$packageDownloadVersion = Resolve-RequestedReleaseVersion -RequestedVersion latest
$packageDownload = Get-ReleaseAssetDownloadDetails -ResolvedVersion $packageDownloadVersion -ResolvedArchitecture x64
[ordered]@{
    helperDownloadUri = [string]$helperDownload.DownloadUri
    helperResolvedVersion = [string]$helperDownload.ResolvedVersion
    packageDownloadUri = [string]$packageDownload.DownloadUri
    packageResolvedVersion = [string]$packageDownload.ResolvedVersion
    releaseChannel = Get-InstallerReleaseChannel
} | ConvertTo-Json -Depth 3
""";

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            using var payload = JsonDocument.Parse(result.Stdout);
            payload.RootElement.GetProperty("helperDownloadUri").GetString().Should().Contain("release_2.0.0-preview.1_win-x64.zip");
            payload.RootElement.GetProperty("helperResolvedVersion").GetString().Should().Be("2.0.0-preview.1");
            payload.RootElement.GetProperty("packageDownloadUri").GetString().Should().Contain("release_2.0.0-preview.1_win-x64.zip");
            payload.RootElement.GetProperty("packageResolvedVersion").GetString().Should().Be("2.0.0-preview.1");
            payload.RootElement.GetProperty("releaseChannel").GetString().Should().Be("prerelease");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }
}
