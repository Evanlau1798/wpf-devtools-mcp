using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    public void OnlineInstallerScript_ShouldDeclareBootstrapProgressAndCliFallbackMessages()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));

        content.Should().Contain("Write-TuiBootstrapMessage");
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

    private static string ComputeManifestCacheKey(string installerDirectory, IReadOnlyCollection<string> helperFiles)
    {
        var records = helperFiles
            .OrderBy(static file => file, StringComparer.OrdinalIgnoreCase)
            .Select(file => file + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(installerDirectory, file)))).ToLowerInvariant())
            .ToArray();
        return "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|", records)))).ToLowerInvariant();
    }
}
