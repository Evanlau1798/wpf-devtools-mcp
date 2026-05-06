using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class OnlineInstallerContractTests
{
    [Fact]
    public void OnlineInstallerScript_ShouldExistAsPublicEntryPoint()
    {
        var scriptPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1");

        File.Exists(scriptPath).Should().BeTrue(
            "users and maintainers should have a stable one-command installer entrypoint under scripts/");
    }

    [Fact]
    public void OnlineInstallerScript_ShouldBeTuiFirstWhileKeepingAutomationFlags()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));

        content.Should().Contain("Start-TuiInstaller");
        content.Should().Contain("[switch]$NonInteractive");
        content.Should().Contain("[switch]$OutputJson");
        content.Should().Contain("Render-TuiScreen");
        content.Should().Contain("Read-TuiKey");
        content.Should().Contain("Read-Host",
            "the installer still needs a plain CLI fallback when the full-screen TUI cannot be used");
    }

    [Fact]
    public void OnlineInstallerScript_ShouldSupportDistinctArchitectureAndWindowsClientSelections()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));

        content.Should().Contain("'x64'");
        content.Should().Contain("'x86'");
        content.Should().Contain("'arm64'");
        content.Should().Contain("'claude-code'");
        content.Should().Contain("'codex'");
        content.Should().Contain("'vscode'");
        content.Should().Contain("'visual-studio'");
    }

    [Fact]
    public void OnlineInstallerScript_ShouldResolveOnlineOfflineModesAndPersistInstallerState()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));

        content.Should().Contain("Resolve-InstallerMode");
        content.Should().Contain("Resolve-InstallerStatePath");
        content.Should().Contain("installer-state.json");
        content.Should().Contain("Save-InstallerState");
        content.Should().Contain("Get-AvailableInstallerUpdates");
        content.Should().Contain("Invoke-TuiUpdateAllOperation");
    }

    [Fact]
    public void OnlineInstallerScript_ShouldDeclareTwoStepConfirmationAndFullUninstallContracts()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));

        content.Should().Contain("ConfirmScreen");
        content.Should().Contain("ConfirmationStep");
        content.Should().Contain("Full Uninstall");
        content.Should().Contain("full-uninstall");
    }

    [Fact]
    public void OnlineInstallerScript_ShouldDownloadVersionedReleaseArchiveNames()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));

        content.Should().Contain("release_{0}_win-{1}.zip");
        content.Should().Contain("releases/latest/download");
        content.Should().Contain("releases/download/");
        content.Should().Contain("api.github.com/repos/Evanlau1798/wpf-devtools-mcp/releases");
    }

    [Fact]
    public void OnlineInstallerScript_ShouldForceTls12BeforeNetworkCalls()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));

        var tlsIndex = content.IndexOf("[Net.ServicePointManager]::SecurityProtocol", StringComparison.Ordinal);
        var firstWebRequestIndex = content.IndexOf("Invoke-WebRequest", StringComparison.Ordinal);
        var firstRestMethodIndex = content.IndexOf("Invoke-RestMethod", StringComparison.Ordinal);

        tlsIndex.Should().BeGreaterThan(0);
        content.Should().Contain("[Net.SecurityProtocolType]::Tls12");
        tlsIndex.Should().BeLessThan(firstWebRequestIndex);
        tlsIndex.Should().BeLessThan(firstRestMethodIndex);
    }

    [Fact]
    public void OnlineInstallerScript_ShouldAvoidLegacyDecorativeCliBranding()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));

        content.Should().NotContain("WPF DEVTOOLS MCP");
        content.Should().NotContain("<Binding Path=\"{Binding}\" />");
        content.Should().NotContain("<DependencyProperty/>");
        content.Should().NotContain("Open docs homepage");
        content.Should().NotContain("WindowChrome.WindowChrome");
    }

    [Fact]
    public void OnlineInstallerScript_ShouldOffloadBootstrapUiHelpersIntoInstallerModules()
    {
        var scriptPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1");
        var content = File.ReadAllText(scriptPath);
        var manifestContent = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/installer-helpers.manifest.json"));

        content.Should().Contain("Installer.BootstrapUi.ps1");
        manifestContent.Should().Contain("Installer.BootstrapUi.ps1");
        manifestContent.Should().Contain("Installer.Actions.ps1");
        manifestContent.Should().Contain("Installer.Uninstall.ps1");
    }

    [Fact]
    public void OnlineInstallerScript_ShouldNotHardRequireSharedModulesBeforeStandaloneNonInteractiveRemoval()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));

        content.Should().NotContain("Assert-InstallerHelperRuntimeAvailable -ResolvedAction $ResolvedAction\r\n    foreach ($helperPath in @(Get-InstallerSharedModulePaths))",
            "standalone noninteractive uninstall/full-uninstall must have a recovery path that does not hard-fail before it can inspect state or existing registration artifacts");
    }

    [Fact]
    public void OnlineInstallerScript_ShouldFindInstallerHelpersInWindowsCreatedArchives()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));

        content.Should().Contain("\"bin\\installer\\$LeafName\"",
            "PowerShell Compress-Archive on Windows stores release entries with backslash separators");
        content.Should().Contain("\"installer\\$LeafName\"",
            "offline package helper lookup should accept both archive layouts supported by packaged releases");
    }
}
