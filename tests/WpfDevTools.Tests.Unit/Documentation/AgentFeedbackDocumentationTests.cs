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
    [InlineData("docfx/agent-feedback/toc.yml", "index.md")]
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

    [Fact]
    public void AgentFeedbackTrackedPages_ShouldNotPublishStaleValidationReports()
    {
        var trackedFiles = GetTrackedAgentFeedbackFiles();

        trackedFiles.Should().BeEquivalentTo(
            [
                "docfx/agent-feedback/index.md",
                "docfx/agent-feedback/template.md",
                "docfx/zh-tw/agent-feedback/index.md",
                "docfx/zh-tw/agent-feedback/template.md"
            ],
            "only the official entry and template pages should be tracked; validation reports must be current before publication");
        trackedFiles.Should().NotContain(path => path.Contains("63-tool", StringComparison.OrdinalIgnoreCase));
    }

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);

    private static string[] GetTrackedAgentFeedbackFiles()
    {
        using var process = new System.Diagnostics.Process();
        process.StartInfo.FileName = "git";
        process.StartInfo.ArgumentList.Add("ls-files");
        process.StartInfo.ArgumentList.Add("docfx/agent-feedback");
        process.StartInfo.ArgumentList.Add("docfx/zh-tw/agent-feedback");
        process.StartInfo.WorkingDirectory = Path.GetDirectoryName(GetRepoFilePath(".gitignore"));
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.CreateNoWindow = true;

        process.Start();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        process.ExitCode.Should().Be(0);

        return output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(path => path.Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }
}
