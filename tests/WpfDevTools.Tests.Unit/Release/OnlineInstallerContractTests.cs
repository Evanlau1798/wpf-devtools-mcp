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
    public void OnlineInstallerScript_ShouldDefaultToLatestVersionAndInteractiveClientSelection()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));

        content.Should().Contain("[string]$Version = 'latest'");
        content.Should().Contain("[switch]$NonInteractive");
        content.Should().Contain("Read-Host");
    }

    [Fact]
    public void OnlineInstallerScript_ShouldSupportKnownArchitectureAndClientSelections()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));

        content.Should().Contain("'x64'");
        content.Should().Contain("'x86'");
        content.Should().Contain("'arm64'");
        content.Should().Contain("'claude-code'");
        content.Should().Contain("'codex'");
        content.Should().Contain("'visual-studio'");
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
    public void OnlineInstallerScript_ShouldDocumentMenuBrandingAndDocsHomepageAction()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));

        content.Should().Contain("WPF DEVTOOLS MCP");
        content.Should().Contain("Open docs homepage");
        content.Should().Contain("https://evanlau1798.github.io/wpf-devtools-mcp/index.html");
    }
}
