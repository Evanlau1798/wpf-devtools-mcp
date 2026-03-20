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
    public void OnlineInstallerScript_ShouldBeGuiFirstWhileKeepingAutomationFlags()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));

        content.Should().Contain("Add-Type -AssemblyName PresentationFramework");
        content.Should().Contain("[switch]$NonInteractive");
        content.Should().Contain("[switch]$OutputJson");
        content.Should().Contain("Show-InstallerWindow");
        content.Should().Contain("WindowStyle=\"None\"");
        content.Should().Contain("WindowChrome.WindowChrome");
        content.Should().Contain("DwmMicaHelper");
        content.Should().Contain("Read-Host",
            "the installer still needs a plain CLI fallback when WPF cannot be used");
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
        content.Should().Contain("UpdateAllButton");
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
    public void OnlineInstallerScript_ShouldAvoidLegacyDecorativeCliBranding()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));

        content.Should().NotContain("WPF DEVTOOLS MCP");
        content.Should().NotContain("<Binding Path=\"{Binding}\" />");
        content.Should().NotContain("<DependencyProperty/>");
        content.Should().NotContain("Open docs homepage");
    }
}
