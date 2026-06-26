using FluentAssertions;
using WpfDevTools.Mcp.Server.McpTools;
using WpfDevTools.Tests.Unit.TestSupport;
using Xunit;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class SnapshotRollbackDocumentationTests
{
    private const string ExplicitSnapshotIdChain =
        "capture_state_snapshot -> snapshotId -> get_state_diff -> restore_state_snapshot";

    [Theory]
    [InlineData("docfx/reference/tools/scene-and-state.md")]
    [InlineData("docfx/zh-tw/reference/tools/scene-and-state.md")]
    [InlineData("docfx/guides/common-workflows.md")]
    [InlineData("docfx/zh-tw/guides/common-workflows.md")]
    public void SnapshotWorkflowPages_ShouldShowExplicitSnapshotIdChain(string relativePath)
    {
        var content = File.ReadAllText(TestRepositoryPaths.GetRepoFilePath(relativePath));

        content.Should().Contain(ExplicitSnapshotIdChain);
    }

    [Fact]
    public void SnapshotToolDescriptions_ShouldShowExplicitSnapshotIdChain()
    {
        StateMcpToolDescriptions.CaptureStateSnapshot.Should().Contain(ExplicitSnapshotIdChain);
        SceneDiagnosticsMcpToolDescriptions.GetStateDiff.Should().Contain(ExplicitSnapshotIdChain);
        StateMcpToolDescriptions.RestoreStateSnapshot.Should().Contain(ExplicitSnapshotIdChain);
    }
}
