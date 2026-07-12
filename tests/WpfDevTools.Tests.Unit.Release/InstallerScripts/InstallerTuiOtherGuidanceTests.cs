using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class InstallerTuiOtherGuidanceTests
{
    [Fact]
    public void TuiScreenModel_ShouldDescribeOtherTargetWithArtifactAndDocumentationGuidance()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.ScreenModel.ps1"));

        content.Should().Contain("other.mcpServers.json");
        content.Should().Contain("claude-code.txt");
        content.Should().Contain("codex.txt");
        content.Should().Contain("AI Agent Clients");
    }

}
