using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class AgentFeedbackDocumentationTests
{
    [Theory]
    [InlineData("docfx/agent-feedback/index.md", "Agent Feedback", "Suggested report skeleton", "template.md")]
    [InlineData("docfx/zh-tw/agent-feedback/index.md", "Agent 使用心得", "建議骨架", "template.md")]
    public void AgentFeedbackIndexPages_ShouldExistAndDescribeReportStructure(
        string relativePath,
        string expectedHeading,
        string expectedStructureHeading,
        string expectedTemplateLink)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain(expectedHeading);
        content.Should().Contain(expectedStructureHeading);
        content.Should().Contain(expectedTemplateLink);
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

    [Theory]
    [InlineData("docfx/agent-feedback/template.md", "# Agent Feedback Template")]
    [InlineData("docfx/zh-tw/agent-feedback/template.md", "# Agent 使用心得範本")]
    public void AgentFeedbackTemplates_ShouldExistWithoutTocExposure(
        string relativePath,
        string expectedHeading)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain(expectedHeading);
    }

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);
}
