using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Diagnostics;
using FluentAssertions;
using Xunit;

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
    public void InstallerHelperManifest_ShouldMatchCurrentHelperFiles()
    {
        var installerDirectory = ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer");
        var manifestPath = Path.Combine(installerDirectory, "installer-helpers.manifest.json");
        File.Exists(manifestPath).Should().BeTrue();

        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var helperFiles = manifest.RootElement.GetProperty("helperFiles")
            .EnumerateArray()
            .Select(static entry => entry.GetString())
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
            return;
        }

        var installerDirectory = ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer");
        var manifestPath = Path.Combine(installerDirectory, "installer-helpers.manifest.json");
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var helperFiles = manifest.RootElement.GetProperty("helperFiles")
            .EnumerateArray()
            .Select(static entry => entry.GetString())
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
    public void OnlineInstallerScript_InlineIexExecution_WhenTuiBootstrapFails_ShouldExplainFallbackAndContinueWithCli()
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
            var command = string.Join(" ; ",
            [
                "$env:APPDATA='" + appData.Replace("'", "''") + "'",
                "$env:LOCALAPPDATA='" + localAppData.Replace("'", "''") + "'",
                "$env:USERPROFILE='" + userProfile.Replace("'", "''") + "'",
                "$env:WPFDEVTOOLS_INSTALLER_HELPER_BASE_URI='http://127.0.0.1:9'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_LATEST_VERSION='1.2.3'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_RESPONSES='||||'",
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
        content.Should().Contain("Preparing installer UI... (manifest)");
        content.Should().Contain("Preparing installer UI... (fallback)");
        content.Should().NotContain("('+' + ('-' * $innerWidth) + '+')");
    }

    [Fact]
    public void OnlineInstallerScript_ShouldNotDownloadHelperRuntimeFromMovingMasterBranch()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));

        content.Should().NotContain("/master/scripts/installer");
        content.Should().Contain("Get-ReleaseRawContentBaseUri");
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
    public void OnlineInstallerScript_FunctionDefinitions_ShouldReuseSingleResolvedLatestVersionForHelpersAndPackagePayload()
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
$scriptPath='{{repoScriptPath.Replace("'", "''")}}'
$scriptContent = Get-Content $scriptPath -Raw
$marker = '$selectionContext = Resolve-Selection'
$markerIndex = $scriptContent.LastIndexOf($marker)
if ($markerIndex -lt 0) { throw 'Main script marker not found.' }
$definitions = $scriptContent.Substring(0, $markerIndex)
. ([scriptblock]::Create($definitions)) -Action install -Version latest -Architecture x64 -Client other -InstallRoot '{{Path.Combine(tempRoot, "install-root").Replace("'", "''")}}' -WorkingRoot '{{workingRoot.Replace("'", "''")}}' -NonInteractive
$global:latestCallCount = 0
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
                }
            )
        }
    }

    throw "Unexpected Invoke-RestMethod URI: $Uri"
}
function Invoke-WebRequest {
    param([string]$Uri, [string]$OutFile, $Headers, [int]$TimeoutSec)
    Set-Content -Path $OutFile -Value 'archive' -Encoding UTF8
}
function Expand-Archive {
    param([string]$Path, [string]$DestinationPath, [switch]$Force)
    $binDir = Join-Path $DestinationPath 'bin'
    New-Item -ItemType Directory -Force -Path $binDir | Out-Null
    Set-Content -Path (Join-Path $binDir 'manifest.json') -Value '{"version":"1.2.3"}' -Encoding UTF8
    Set-Content -Path (Join-Path $binDir 'wpf-devtools-x64.exe') -Value 'stub' -Encoding UTF8
}
$helperUri = Resolve-TuiHelperDownloadBaseUri
$session = Resolve-PackageSession -Mode online -ResolvedVersion latest -ResolvedArchitecture x64
[ordered]@{
    helperUri = $helperUri
    packageResolvedVersion = [string]$session.ResolvedVersion
} | ConvertTo-Json -Depth 3
""";

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            using var payload = JsonDocument.Parse(result.Stdout);
            payload.RootElement.GetProperty("helperUri").GetString().Should().Contain("/v1.2.3/scripts/installer");
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
