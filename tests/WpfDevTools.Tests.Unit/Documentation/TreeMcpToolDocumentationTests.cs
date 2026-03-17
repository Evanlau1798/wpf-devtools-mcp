using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class TreeMcpToolDocumentationTests
{
    [Fact]
    public void TreeToolDescriptions_ShouldDescribeCompressedResponseShapes()
    {
        var content = File.ReadAllText(GetRepoFilePath("src/WpfDevTools.Mcp.Server/McpTools/TreeMcpTools.cs"));

        content.Should().Contain("flat-summary-v1");
        content.Should().Contain("returnedNodeCount");
        content.Should().Contain("omittedNodeCount");
        content.Should().Contain("truncated");
        content.Should().Contain("appliedOptions");
        content.Should().Contain("maxChildrenPerNode");
        content.Should().Contain("depthSufficiencyHint");
    }

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);
}
