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
    }

    private static string GetRepoFilePath(string relativePath)
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relativePath));
}
