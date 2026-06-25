using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class InstallerModeDetectionTests
{
    [Fact]
    public void AddInstallerReleaseChannelToResult_WithOfflinePrereleaseResolvedVersion_ShouldReportPrerelease()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var appData = Path.Combine(tempRoot, "AppData", "Roaming");
            var localAppData = Path.Combine(tempRoot, "AppData", "Local");
            var userProfile = Path.Combine(tempRoot, "UserProfile");
            var installRoot = Path.Combine(tempRoot, "install-root");
            var workingRoot = Path.Combine(tempRoot, "working-root");
            Directory.CreateDirectory(appData);
            Directory.CreateDirectory(localAppData);
            Directory.CreateDirectory(userProfile);

            var command = $$"""
$env:APPDATA='{{OnlineInstallerScriptTestHarness.EscapePowerShellSingleQuotedString(appData)}}'
$env:LOCALAPPDATA='{{OnlineInstallerScriptTestHarness.EscapePowerShellSingleQuotedString(localAppData)}}'
$env:USERPROFILE='{{OnlineInstallerScriptTestHarness.EscapePowerShellSingleQuotedString(userProfile)}}'
{{OnlineInstallerScriptTestHarness.BuildDefinitionOnlyPrelude("-Action install -Version latest -Architecture x64 -Client other -InstallRoot '" + OnlineInstallerScriptTestHarness.EscapePowerShellSingleQuotedString(installRoot) + "' -WorkingRoot '" + OnlineInstallerScriptTestHarness.EscapePowerShellSingleQuotedString(workingRoot) + "' -NonInteractive")}}
$resolvedVersionResult = [ordered]@{
    action = 'install'
    mode = 'offline'
    downloadSource = 'local-package'
    version = 'latest'
    resolvedVersion = '1.0.0-beta.1'
}
$assetNameResult = [ordered]@{
    action = 'install'
    mode = 'offline'
    downloadSource = 'local-package'
    version = 'latest'
    resolvedVersion = '1.0.0'
    packageAssetName = 'release_1.0.0-beta.1_win-x64.zip'
    downloadUri = 'https://github.com/Evanlau1798/wpf-devtools-mcp/releases/download/v1.0.0-beta.1/release_1.0.0-beta.1_win-x64.zip'
}
[ordered]@{
    resolvedVersionChannel = (Add-InstallerReleaseChannelToResult -Result $resolvedVersionResult).releaseChannel
    assetNameChannel = (Add-InstallerReleaseChannelToResult -Result $assetNameResult).releaseChannel
} | ConvertTo-Json -Depth 3
""";

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            using var json = JsonDocument.Parse(result.Stdout);
            json.RootElement.GetProperty("resolvedVersionChannel").GetString().Should().Be("prerelease");
            json.RootElement.GetProperty("assetNameChannel").GetString().Should().Be("prerelease");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstaller_WithLocalArchiveInput_ShouldReportOfflineLocalPackageMode()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot, "x64");
            var installRoot = Path.Combine(tempRoot, "install-root");

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                new[]
                {
                    "-PackageArchivePath", archivePath,
                    "-InstallRoot", installRoot,
                    "-Client", "other",
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                },
                new Dictionary<string, string?>
                {
                    ["APPDATA"] = Path.Combine(tempRoot, "AppData", "Roaming"),
                    ["LOCALAPPDATA"] = Path.Combine(tempRoot, "AppData", "Local"),
                    ["USERPROFILE"] = Path.Combine(tempRoot, "UserProfile"),
                    ["PATH"] = string.Empty
                });

            result.ExitCode.Should().Be(0, result.Stderr);
            using var json = JsonDocument.Parse(result.Stdout);
            json.RootElement.GetProperty("mode").GetString().Should().Be("offline");
            json.RootElement.GetProperty("downloadSource").GetString().Should().Be("local-package");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }
}
