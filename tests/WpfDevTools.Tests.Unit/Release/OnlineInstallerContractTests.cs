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
    public void OnlineInstallerScript_ShouldDefaultToLatestVersionAndX64ClaudeCode()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));

        content.Should().Contain("[string]$Version = 'latest'");
        content.Should().Contain("[string]$Architecture = 'x64'");
        content.Should().Contain("[string]$Client = 'claude-code'");
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
        content.Should().Contain("'codex-cli'");
        content.Should().Contain("'claude-desktop'");
        content.Should().Contain("'cursor-vscode'");
        content.Should().Contain("'github-copilot-vscode'");
        content.Should().Contain("'other'");
    }

    [Fact]
    public void OnlineInstallerScript_ShouldDownloadVersionedReleaseArchiveNames()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));

        content.Should().Contain("release_{0}_win-{1}.zip");
        content.Should().Contain("releases/latest/download");
        content.Should().Contain("releases/download/");
    }
}
