using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class InstallerModeDetectionTests
{
    [Fact]
    public void FullUninstallSummary_ShouldDescribeSinglePrereleaseAndMixedRemovals()
    {
        var actionsCore = OnlineInstallerScriptTestHarness.EscapePowerShellSingleQuotedString(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Installer.Actions.Core.ps1"));
        var stateHelpers = OnlineInstallerScriptTestHarness.EscapePowerShellSingleQuotedString(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Installer.State.Installation.ps1"));
        var command = $$"""
. '{{stateHelpers}}'
. '{{actionsCore}}'
$single = Get-InstallerFullUninstallResultSummary -RemovedInstallations @(
    [ordered]@{ ResolvedVersion='1.0.0-beta.36'; InstallRoot='C:\single'; InstallBase='C:\single\x64' }
)
$mixed = Get-InstallerFullUninstallResultSummary -RemovedInstallations @(
    [ordered]@{ ResolvedVersion='1.0.0'; InstallRoot='C:\stable'; InstallBase='C:\stable\x64' },
    [ordered]@{ ResolvedVersion='1.0.0-beta.36'; InstallRoot='C:\preview'; InstallBase='C:\preview\x64' }
)
[ordered]@{ single=$single; mixed=$mixed } | ConvertTo-Json -Depth 5
""";

        var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

        result.ExitCode.Should().Be(0, result.Stderr);
        using var json = JsonDocument.Parse(result.Stdout);
        var single = json.RootElement.GetProperty("single");
        single.GetProperty("version").GetString().Should().Be("1.0.0-beta.36");
        single.GetProperty("resolvedVersion").GetString().Should().Be("1.0.0-beta.36");
        single.GetProperty("installRoot").GetString().Should().Be(@"C:\single");
        single.GetProperty("releaseChannel").GetString().Should().Be("prerelease");

        var mixed = json.RootElement.GetProperty("mixed");
        mixed.GetProperty("version").GetString().Should().Be("multiple");
        mixed.GetProperty("resolvedVersion").ValueKind.Should().Be(JsonValueKind.Null);
        mixed.GetProperty("installRoot").ValueKind.Should().Be(JsonValueKind.Null);
        mixed.GetProperty("releaseChannel").GetString().Should().Be("mixed");
    }

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
            var profile = json.RootElement.GetProperty("composerPolicyProfile");
            profile.GetProperty("purpose").GetString().Should().Be("ui-composer-project-writes");
            profile.GetProperty("freshServerProcessRequired").GetBoolean().Should().BeTrue();
            var requiredEnvironment = profile.GetProperty("requiredEnvironment");
            requiredEnvironment.GetProperty("WPFDEVTOOLS_MCP_ALLOW_PROJECT_WRITES").GetString().Should().Be("true");
            requiredEnvironment.GetProperty("WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS").GetString().Should().Be("true");
            requiredEnvironment.GetProperty("WPFDEVTOOLS_MCP_ALLOWED_PROJECT_ROOTS").GetString()
                .Should().Be("<exact absolute WPF project root>");

            var serverCommand = json.RootElement.GetProperty("serverCommand");
            serverCommand.GetProperty("executable").GetString().Should().Be(
                Path.Combine(installRoot, "x64", "current", "bin", "wpf-devtools-x64.exe"));
            serverCommand.GetProperty("arguments").GetArrayLength().Should().Be(0);
            serverCommand.GetProperty("transport").GetString().Should().Be("stdio");
            serverCommand.GetProperty("client").GetString().Should().Be("other");
            serverCommand.GetProperty("architecture").GetString().Should().Be("x64");
            serverCommand.GetProperty("installRoot").GetString().Should().Be(installRoot);
            serverCommand.GetProperty("freshServerProcessRequired").GetBoolean().Should().BeTrue();

            var policyTemplate = serverCommand.GetProperty("policyTemplate");
            policyTemplate.GetProperty("WPFDEVTOOLS_MCP_ALLOWED_TARGETS").GetString()
                .Should().Be("<exact absolute WPF target executable path>");
            policyTemplate.GetProperty("WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS").GetString()
                .Should().Be("<same exact target path only when raw injection is required>");
            policyTemplate.GetProperty("WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS").GetString().Should().Be("true");
            serverCommand.GetRawText().Should().NotContain("AUTH_SECRET").And.NotContain("CERT_DIR");
            serverCommand.GetRawText().Should().NotContain("wpfui").And.NotContain("WPF-UI");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }
}
