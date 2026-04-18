using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Diagnostics;
using FluentAssertions;
using Xunit;
using Xunit.Sdk;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class InstallerBootstrapTests
{
    [Fact]
    public void OnlineInstallerScript_ShouldDeferLatestVersionLookupUntilAfterTuiStartup()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));

        content.Should().Contain("Initialize-TuiStartupState");
        content.Should().Contain("Get-LatestInstallerVersion -UseCacheOnly");
    }

    [Fact]
    public void TuiFlow_ShouldEnterTerminalSessionBeforeRunningStartupInitialization()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Flow.ps1"));

        var startFunctionIndex = content.IndexOf("function Start-TuiInstallerCore", StringComparison.Ordinal);
        startFunctionIndex.Should().BeGreaterThanOrEqualTo(0);

        var startFunctionBody = content[startFunctionIndex..];
        var enterSessionIndex = startFunctionBody.IndexOf("Enter-TuiTerminalSessionCore", StringComparison.Ordinal);
        var initializeIndex = startFunctionBody.IndexOf("Initialize-TuiStartupStateCore -State $state", StringComparison.Ordinal);

        enterSessionIndex.Should().BeGreaterThanOrEqualTo(0);
        initializeIndex.Should().BeGreaterThanOrEqualTo(0);
        enterSessionIndex.Should().BeLessThan(initializeIndex);
    }

    [Fact]
    public void OnlineInstallerScript_ShouldDeclareBootstrapProgressAndCliFallbackMessages()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));

        content.Should().Contain("Write-TuiBootstrapScreen");
        content.Should().Contain("Preparing installer UI...");
        content.Should().Contain("Installer UI bootstrap failed. Falling back to plain CLI.");
    }

    [Fact]
    public void OnlineInstallerScript_ShouldUseManifestBackedHelperCacheKeys()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));

        content.Should().Contain("installer-helpers.manifest.json");
        content.Should().Contain("Get-InstallerHelperRuntimeCacheKey");
        content.Should().NotContain("WPFDEVTOOLS_INSTALLER_HELPER_CACHE_KEY:v1");
    }

    [Fact]
    public void OnlineInstallerScript_ShouldRejectHelperOverrideOutsideTestMode()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var helperDirectory = ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer");
            var command = string.Join(" ; ",
            [
                "$env:WPFDEVTOOLS_INSTALLER_HELPER_DIRECTORY='" + helperDirectory.Replace("'", "''") + "'",
                ". ([scriptblock]::Create((Get-Content '" + ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1").Replace("'", "''") + "' -Raw))) -Action install -Architecture x64 -Client other -NonInteractive",
                "Get-TuiHelperOverrideDirectory"
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(
                command,
                new Dictionary<string, string?>
                {
                    ["APPDATA"] = Path.Combine(tempRoot, "AppData", "Roaming"),
                    ["LOCALAPPDATA"] = Path.Combine(tempRoot, "AppData", "Local"),
                    ["USERPROFILE"] = Path.Combine(tempRoot, "UserProfile"),
                    ["WPFDEVTOOLS_INSTALLER_TEST_MODE"] = "0"
                });

            result.ExitCode.Should().NotBe(0);
            result.Stderr.Should().Contain("WPFDEVTOOLS_INSTALLER_HELPER_DIRECTORY is supported only when WPFDEVTOOLS_INSTALLER_TEST_MODE=1");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstallerScript_ShouldRejectHelperBaseUriOverrideOutsideTestMode()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var repoScriptPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1");
            var command = string.Join(" ; ",
            [
                "$env:WPFDEVTOOLS_INSTALLER_HELPER_BASE_URI='http://127.0.0.1:9/installer'",
                "$scriptPath='" + repoScriptPath.Replace("'", "''") + "'",
                "$scriptContent = Get-Content $scriptPath -Raw",
                "$marker = '$selectionContext = Resolve-Selection'",
                "$markerIndex = $scriptContent.LastIndexOf($marker)",
                "if ($markerIndex -lt 0) { throw 'Main script marker not found.' }",
                "$definitions = $scriptContent.Substring(0, $markerIndex)",
                ". ([scriptblock]::Create($definitions)) -Action install -Architecture x64 -Client other -NonInteractive",
                "Resolve-TuiHelperDownloadBaseUri"
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(
                command,
                new Dictionary<string, string?>
                {
                    ["APPDATA"] = Path.Combine(tempRoot, "AppData", "Roaming"),
                    ["LOCALAPPDATA"] = Path.Combine(tempRoot, "AppData", "Local"),
                    ["USERPROFILE"] = Path.Combine(tempRoot, "UserProfile"),
                    ["WPFDEVTOOLS_INSTALLER_TEST_MODE"] = "0"
                });

            result.ExitCode.Should().NotBe(0);
            result.Stderr.Should().Contain("WPFDEVTOOLS_INSTALLER_HELPER_BASE_URI is supported only when WPFDEVTOOLS_INSTALLER_TEST_MODE=1");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void InstallerHelperManifest_ShouldMatchCurrentHelperFiles()
    {
        var installerDirectory = ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer");
        var manifestPath = Path.Combine(installerDirectory, "installer-helpers.manifest.json");
        File.Exists(manifestPath).Should().BeTrue();

        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var helperFiles = manifest.RootElement.GetProperty("helperFiles")
            .EnumerateArray()
            .Select(static entry => entry.ValueKind == JsonValueKind.Object
                ? entry.GetProperty("path").GetString()
                : entry.GetString())
            .Where(static entry => !string.IsNullOrWhiteSpace(entry))
            .Cast<string>()
            .ToArray();

        helperFiles.Should().NotBeEmpty();
        foreach (var helperFile in helperFiles)
        {
            File.Exists(Path.Combine(installerDirectory, helperFile)).Should().BeTrue();
        }

        var expectedCacheKey = ComputeManifestCacheKey(installerDirectory, helperFiles);
        manifest.RootElement.GetProperty("cacheKey").GetString().Should().Be(expectedCacheKey);
    }

    [Fact]
    public void InstallerHelperManifest_ShouldOnlyReferenceGitTrackedHelperFiles()
    {
        var repoRoot = ReleaseScriptTestHarness.GetRepoFilePath(".");
        if (!CanValidateTrackedFilesWithGit(repoRoot))
        {
            throw SkipException.ForSkip("Git metadata or the git executable is unavailable, so tracked-file validation cannot run in this environment.");
        }

        var installerDirectory = ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer");
        var manifestPath = Path.Combine(installerDirectory, "installer-helpers.manifest.json");
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var helperFiles = manifest.RootElement.GetProperty("helperFiles")
            .EnumerateArray()
            .Select(static entry => entry.ValueKind == JsonValueKind.Object
                ? entry.GetProperty("path").GetString()
                : entry.GetString())
            .Where(static entry => !string.IsNullOrWhiteSpace(entry))
            .Cast<string>()
            .ToArray();

        foreach (var helperFile in helperFiles)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = repoRoot,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("ls-files");
            startInfo.ArgumentList.Add("--error-unmatch");
            startInfo.ArgumentList.Add(Path.Combine("scripts", "installer", helperFile).Replace('\\', '/'));

            using var process = Process.Start(startInfo)!;
            process.WaitForExit();
            process.ExitCode.Should().Be(0, $"{helperFile} must be tracked because the installer manifest ships it");
        }
    }

    [Fact]
    public void InstallerHelperManifestIntegrity_ShouldFail_WhenAHelperRecordIsMissing()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var helperRoot = Path.Combine(tempRoot, "installer");
            Directory.CreateDirectory(helperRoot);

            var sourceHelperRoot = ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer");
            foreach (var sourcePath in Directory.GetFiles(sourceHelperRoot, "*.ps1"))
            {
                File.Copy(sourcePath, Path.Combine(helperRoot, Path.GetFileName(sourcePath)), overwrite: true);
            }

            var manifestPath = Path.Combine(helperRoot, "installer-helpers.manifest.json");
            var manifestNode = JsonNode.Parse(File.ReadAllText(Path.Combine(sourceHelperRoot, "installer-helpers.manifest.json")))!.AsObject();
            var helperFiles = manifestNode["helperFiles"]!.AsArray();
            var filteredHelperFiles = new JsonArray();
            foreach (var entry in helperFiles)
            {
                if (entry is null)
                {
                    continue;
                }

                var path = entry["path"]?.GetValue<string>() ?? entry.GetValue<string>();
                if (string.Equals(path, "Installer.Actions.ps1", StringComparison.Ordinal))
                {
                    continue;
                }

                filteredHelperFiles.Add(entry.DeepClone());
            }

            manifestNode["helperFiles"] = filteredHelperFiles;
            File.WriteAllText(manifestPath, manifestNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

            var repoScriptPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1");
            var command = $$"""
$env:APPDATA='{{Path.Combine(tempRoot, "AppData", "Roaming").Replace("'", "''")}}'
$env:LOCALAPPDATA='{{Path.Combine(tempRoot, "AppData", "Local").Replace("'", "''")}}'
$env:USERPROFILE='{{Path.Combine(tempRoot, "UserProfile").Replace("'", "''")}}'
$scriptPath='{{repoScriptPath.Replace("'", "''")}}'
$scriptContent = Get-Content $scriptPath -Raw
$marker = '$selectionContext = Resolve-Selection'
$markerIndex = $scriptContent.LastIndexOf($marker)
if ($markerIndex -lt 0) { throw 'Main script marker not found.' }
$definitions = $scriptContent.Substring(0, $markerIndex)
. ([scriptblock]::Create($definitions)) -Action install -Version latest -Architecture x64 -Client other -NonInteractive
$manifest = Read-TuiHelperManifest -ManifestPath '{{manifestPath.Replace("'", "''")}}' -HelperDirectory '{{helperRoot.Replace("'", "''")}}'
Assert-InstallerHelperManifestIntegrity -HelperDirectory '{{helperRoot.Replace("'", "''")}}' -Manifest $manifest
""";

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().NotBe(0);
            (result.Stdout + Environment.NewLine + result.Stderr)
                .Should().MatchRegex("must exactly match the expected helper file set|missing integrity metadata",
                    "bootstrap should fail before loading helper code when the manifest no longer provides a complete integrity record set");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstallerScript_InlineIexExecution_WhenTuiBootstrapFails_ShouldExplainFallbackAndContinueWithCli()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var appData = Path.Combine(tempRoot, "AppData", "Roaming");
            var localAppData = Path.Combine(tempRoot, "AppData", "Local");
            var userProfile = Path.Combine(tempRoot, "UserProfile");
            var installRootResponse = Path.Combine(appData, "WpfDevToolsMcp");
            Directory.CreateDirectory(appData);
            Directory.CreateDirectory(localAppData);
            Directory.CreateDirectory(userProfile);

            var repoScriptPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1");
            var command = string.Join(" ; ",
            [
                "$env:APPDATA='" + appData.Replace("'", "''") + "'",
                "$env:LOCALAPPDATA='" + localAppData.Replace("'", "''") + "'",
                "$env:USERPROFILE='" + userProfile.Replace("'", "''") + "'",
                "$env:WPFDEVTOOLS_INSTALLER_HELPER_BASE_URI='http://127.0.0.1:9'",
                "$env:WPFDEVTOOLS_INSTALLER_HELPER_TIMEOUT_SEC='1'",
                "$env:WPFDEVTOOLS_INSTALLER_HELPER_BOOTSTRAP_TIMEOUT_SEC='3'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_LATEST_VERSION='1.2.3'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_RESPONSES='uninstall||x64||other||" + installRootResponse.Replace("'", "''") + "'",
                "Set-Location '" + tempRoot.Replace("'", "''") + "'",
                "& ([scriptblock]::Create((Get-Content '" + repoScriptPath.Replace("'", "''") + "' -Raw))) -Action uninstall -Architecture x64 -Client other"
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().NotBe(0);
            result.Stderr.Should().Contain("The installer runtime required for uninstall is unavailable.");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstallerScript_ShouldDeclareBootstrapPseudoWindowBeforeHelperDownloadCompletes()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));

        content.Should().Contain("Write-TuiBootstrapScreen");
        content.Should().Contain("Enter-TuiBootstrapTerminalSession");
        content.Should().Contain("Exit-TuiBootstrapTerminalSession");
        content.Should().Contain("Preparing installer UI... (archive)");
        content.Should().Contain("Preparing installer UI... (fallback)");
        content.Should().NotContain("('+' + ('-' * $innerWidth) + '+')");
    }

    [Fact]
    public void OnlineInstallerScript_ShouldBootstrapHelperRuntimeFromReleaseArchives()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));

        content.Should().Contain("Get-TuiHelperArchiveDownloadDetails");
        content.Should().Contain("Assert-TuiHelperArchiveIntegrity");
        content.Should().Contain("Copy-InstallerHelperBundleFromArchive");
    }

    [Fact]
    public void StartLatestInstallerVersionRefresh_ShouldEscapeSingleQuotedPathsAndUrisInBackgroundCommand()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var quotedTempRoot = Path.Combine(tempRoot, "user's-temp");
            Directory.CreateDirectory(quotedTempRoot);
            var repoScriptPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1");
            var command = $$"""
$env:APPDATA='{{Path.Combine(tempRoot, "AppData", "Roaming").Replace("'", "''")}}'
$env:LOCALAPPDATA='{{Path.Combine(tempRoot, "AppData", "Local").Replace("'", "''")}}'
$env:USERPROFILE='{{Path.Combine(tempRoot, "UserProfile").Replace("'", "''")}}'
$env:TEMP='{{quotedTempRoot.Replace("'", "''")}}'
$scriptPath='{{repoScriptPath.Replace("'", "''")}}'
$scriptContent = Get-Content $scriptPath -Raw
$marker = '$selectionContext = Resolve-Selection'
$markerIndex = $scriptContent.LastIndexOf($marker)
if ($markerIndex -lt 0) { throw 'Main script marker not found.' }
$definitions = $scriptContent.Substring(0, $markerIndex)
. ([scriptblock]::Create($definitions)) -Action install -Version latest -Architecture x64 -Client other -InstallRoot '{{Path.Combine(tempRoot, "install-root").Replace("'", "''")}}' -WorkingRoot '{{Path.Combine(tempRoot, "working").Replace("'", "''")}}' -NonInteractive
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
            result.Stdout.Should().Contain("user''s-temp");
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

            var repoScriptPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1");
            var command = $$"""
$env:APPDATA='{{appData.Replace("'", "''")}}'
$env:LOCALAPPDATA='{{localAppData.Replace("'", "''")}}'
$env:USERPROFILE='{{userProfile.Replace("'", "''")}}'
$scriptPath='{{repoScriptPath.Replace("'", "''")}}'
$scriptContent = Get-Content $scriptPath -Raw
$marker = '$selectionContext = Resolve-Selection'
$markerIndex = $scriptContent.LastIndexOf($marker)
if ($markerIndex -lt 0) { throw 'Main script marker not found.' }
$definitions = $scriptContent.Substring(0, $markerIndex)
. ([scriptblock]::Create($definitions)) -Action install -Version latest -Architecture x64 -Client other -InstallRoot '{{Path.Combine(tempRoot, "install-root").Replace("'", "''")}}' -WorkingRoot '{{Path.Combine(tempRoot, "working").Replace("'", "''")}}' -NonInteractive
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

            var repoScriptPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1");
            var command = $$"""
$env:APPDATA='{{appData.Replace("'", "''")}}'
$env:LOCALAPPDATA='{{localAppData.Replace("'", "''")}}'
$env:USERPROFILE='{{userProfile.Replace("'", "''")}}'
$env:TEMP='{{tempRoot.Replace("'", "''")}}'
$env:WPFDEVTOOLS_INSTALLER_HELPER_DIRECTORY='{{ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer").Replace("'", "''")}}'
$scriptPath='{{repoScriptPath.Replace("'", "''")}}'
$scriptContent = Get-Content $scriptPath -Raw
$marker = '$selectionContext = Resolve-Selection'
$markerIndex = $scriptContent.LastIndexOf($marker)
if ($markerIndex -lt 0) { throw 'Main script marker not found.' }
$definitions = $scriptContent.Substring(0, $markerIndex)
. ([scriptblock]::Create($definitions)) -Action install -Version latest -Architecture x64 -Client other -InstallRoot '{{Path.Combine(tempRoot, "install-root").Replace("'", "''")}}' -WorkingRoot '{{workingRoot.Replace("'", "''")}}' -NonInteractive
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
    public void OnlineInstallerScript_InlineIexExecution_ShouldRenderFullscreenBootstrapLoadingScreen()
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

            var repoScriptPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1");
            var helperDirectory = ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer");
            var command = string.Join(" ; ",
            [
                "$env:APPDATA='" + appData.Replace("'", "''") + "'",
                "$env:LOCALAPPDATA='" + localAppData.Replace("'", "''") + "'",
                "$env:USERPROFILE='" + userProfile.Replace("'", "''") + "'",
                "$env:WPFDEVTOOLS_INSTALLER_HELPER_DIRECTORY='" + helperDirectory.Replace("'", "''") + "'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_TUI_KEYS='Escape||Enter'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_DISABLE_CLEAR='1'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_DISABLE_ANSI='1'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_CONSOLE_WIDTH='96'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_CONSOLE_HEIGHT='28'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_LATEST_VERSION='1.2.3'",
                "Set-Location '" + tempRoot.Replace("'", "''") + "'",
                "& ([scriptblock]::Create((Get-Content '" + repoScriptPath.Replace("'", "''") + "' -Raw))) -Action install -Architecture x64 -Client other"
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stdout.Should().Contain("Preparing installer UI");
            result.Stdout.Should().Contain("[Status] Preparing installer UI...");
            result.Stdout.Should().NotContain("Status: Preparing installer UI...");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    private static string ComputeManifestCacheKey(string installerDirectory, IReadOnlyCollection<string> helperFiles)
    {
        var records = helperFiles
            .OrderBy(static file => file, StringComparer.OrdinalIgnoreCase)
            .Select(file => file + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(installerDirectory, file)))).ToLowerInvariant())
            .ToArray();
        return "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|", records)))).ToLowerInvariant();
    }

    private static bool CanValidateTrackedFilesWithGit(string repoRoot)
    {
        var gitMetadataPath = Path.Combine(repoRoot, ".git");
        if (!Directory.Exists(gitMetadataPath) && !File.Exists(gitMetadataPath))
        {
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = repoRoot,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("--version");

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            if (!process.WaitForExit(5000))
            {
                process.Kill(entireProcessTree: true);
                return false;
            }

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
