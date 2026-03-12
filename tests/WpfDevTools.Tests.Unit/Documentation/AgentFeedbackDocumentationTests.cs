using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class AgentFeedbackDocumentationTests
{
    [Theory]
    [InlineData("docfx/agent-feedback/index.md", "Agent Feedback", "Suggested report skeleton")]
    [InlineData("docfx/zh-tw/agent-feedback/index.md", "Agent 使用心得", "建議骨架")]
    public void AgentFeedbackIndexPages_ShouldExistAndDescribeReportStructure(
        string relativePath,
        string expectedHeading,
        string expectedStructureHeading)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain(expectedHeading);
        content.Should().Contain(expectedStructureHeading);
    }

    [Theory]
    [InlineData("docfx/toc.yml", "agent-feedback/index.md")]
    [InlineData("docfx/zh-tw/toc.yml", "agent-feedback/index.md")]
    public void Tocs_ShouldExposeAgentFeedbackSection(
        string relativePath,
        string indexHref)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain(indexHref);
    }

    private static string GetRepoFilePath(string relativePath)
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relativePath));
}
