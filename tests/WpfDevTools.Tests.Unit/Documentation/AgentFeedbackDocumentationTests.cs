using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class AgentFeedbackDocumentationTests
{
    [Theory]
    [InlineData("docfx/agent-feedback", "docfx/agent-feedback/index.md", "Agent Feedback")]
    [InlineData("docfx/zh-tw/agent-feedback", "docfx/zh-tw/agent-feedback/index.md", "Agent 使用心得")]
    public void AgentFeedbackIndexPages_ShouldRegisterAllPublishedReports(
        string directoryPath,
        string indexPath,
        string expectedHeading)
    {
        var content = File.ReadAllText(GetRepoFilePath(indexPath));
        var reportFiles = GetAgentFeedbackReportFileNames(directoryPath);
        var indexLinks = ExtractMarkdownReportLinks(content);

        content.Should().Contain(expectedHeading);
        indexLinks.Should().BeEquivalentTo(
            reportFiles,
            "each Agent Feedback report in the folder should be registered in the locale index");
        content.Should().NotContain("Template");
        content.Should().NotContain("範本");
        content.Should().NotContain("Suggested report skeleton");
        content.Should().NotContain("建議骨架");
    }

    [Theory]
    [InlineData("docfx/toc.yml", "agent-feedback/index.md")]
    [InlineData("docfx/zh-tw/toc.yml", "agent-feedback/index.md")]
    public void PublicTocs_ShouldExposeAgentFeedbackIndex(
        string relativePath,
        string indexHref)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain(indexHref);
    }

    [Fact]
    public void DocfxBuild_ShouldIncludeAgentFeedbackPages()
    {
        var content = File.ReadAllText(GetRepoFilePath("docfx/docfx.json"));

        content.Should().NotContain("agent-feedback/**");
        content.Should().NotContain("zh-tw/agent-feedback/**");
    }

    [Fact]
    public void AgentFeedbackToc_ShouldRegisterAllPublishedEnglishReports()
    {
        var content = File.ReadAllText(GetRepoFilePath("docfx/agent-feedback/toc.yml"));
        var reportFiles = GetAgentFeedbackReportFileNames("docfx/agent-feedback");
        var tocLinks = ExtractYamlReportHrefs(content);

        tocLinks.Should().BeEquivalentTo(
            reportFiles,
            "the Agent Feedback toc should expose every report in the folder");
    }

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);

    private static string[] GetAgentFeedbackReportFileNames(string directoryPath)
        => Directory.EnumerateFiles(GetRepoFilePath(directoryPath), "*.md", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => !string.Equals(name, "index.md", StringComparison.OrdinalIgnoreCase))
            .Select(name => name!)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static string[] ExtractMarkdownReportLinks(string content)
        => content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(ExtractMarkdownHref)
            .Where(IsReportHref)
            .Select(href => href!)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static string[] ExtractYamlReportHrefs(string content)
        => content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("href:", StringComparison.Ordinal))
            .Select(line => line["href:".Length..].Trim().Trim('"', '\''))
            .Where(IsReportHref)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static string? ExtractMarkdownHref(string line)
    {
        var markerIndex = line.IndexOf("](", StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return null;
        }

        var hrefStart = markerIndex + 2;
        var hrefEnd = line.IndexOf(')', hrefStart);
        return hrefEnd > hrefStart ? line[hrefStart..hrefEnd] : null;
    }

    private static bool IsReportHref(string? href)
        => href is not null
           && href.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
           && !string.Equals(href, "index.md", StringComparison.OrdinalIgnoreCase);
}
