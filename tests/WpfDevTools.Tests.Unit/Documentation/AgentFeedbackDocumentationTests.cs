using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class AgentFeedbackDocumentationTests
{
    [Theory]
    [InlineData("docfx/agent-feedback/index.md", "Agent Feedback", "2026-03-17-agent-feedback-63-tool-e2e-validation.md", "2026-06-24-agent-feedback-security-deep-scan.md", "2026-06-29-agent-feedback-mahapps-real-project-e2e.md", "2026-06-29-agent-feedback-materialdesign-real-project-e2e.md", "2026-06-30-agent-feedback-handycontrol-real-project-e2e.md", "2026-06-30-agent-feedback-wpfui-edgecase-e2e.md")]
    [InlineData("docfx/zh-tw/agent-feedback/index.md", "Agent 使用心得", "2026-03-17-agent-feedback-63-tool-e2e-validation.md", "2026-06-24-agent-feedback-security-deep-scan.md", "2026-06-29-agent-feedback-mahapps-real-project-e2e.md", "2026-06-29-agent-feedback-materialdesign-real-project-e2e.md", "2026-06-30-agent-feedback-handycontrol-real-project-e2e.md", "2026-06-30-agent-feedback-wpfui-edgecase-e2e.md")]
    public void AgentFeedbackIndexPages_ShouldBeEntryPagesOnly(
        string relativePath,
        string expectedHeading,
        string firstReportHref,
        string secondReportHref,
        string thirdReportHref,
        string fourthReportHref,
        string fifthReportHref,
        string sixthReportHref)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain(expectedHeading);
        content.Should().Contain(firstReportHref);
        content.Should().Contain(secondReportHref);
        content.Should().Contain(thirdReportHref);
        content.Should().Contain(fourthReportHref);
        content.Should().Contain(fifthReportHref);
        content.Should().Contain(sixthReportHref);
        content.Should().NotContain("Template");
        content.Should().NotContain("範本");
        content.Should().NotContain("Suggested report skeleton");
        content.Should().NotContain("建議骨架");
        content.Length.Should().BeLessThan(1200, "the index page should remain a compact document entry point");
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
    public void AgentFeedbackTrackedPages_ShouldContainOnlyApprovedReportsAndIndexes()
    {
        var trackedFiles = GetTrackedAgentFeedbackFiles();

        trackedFiles.Should().BeEquivalentTo(
            [
                "docfx/agent-feedback/2026-03-17-agent-feedback-63-tool-e2e-validation.md",
                "docfx/agent-feedback/2026-06-24-agent-feedback-security-deep-scan.md",
                "docfx/agent-feedback/2026-06-29-agent-feedback-mahapps-real-project-e2e.md",
                "docfx/agent-feedback/2026-06-29-agent-feedback-materialdesign-real-project-e2e.md",
                "docfx/agent-feedback/2026-06-30-agent-feedback-handycontrol-real-project-e2e.md",
                "docfx/agent-feedback/2026-06-30-agent-feedback-wpfui-edgecase-e2e.md",
                "docfx/agent-feedback/assets/2026-06-29-mahapps-focused-screenshot.png",
                "docfx/agent-feedback/assets/2026-06-29-mahapps-root-window.png",
                "docfx/agent-feedback/assets/2026-06-29-materialdesign-main-window.png",
                "docfx/agent-feedback/assets/2026-06-29-materialdesign-mcp-screenshot.png",
                "docfx/agent-feedback/assets/handycontrol-2026-06-30/main-window.png",
                "docfx/agent-feedback/assets/handycontrol-2026-06-30/mcp-element-screenshot.png",
                "docfx/agent-feedback/assets/handycontrol-2026-06-30/post-mutation-window.png",
                "docfx/agent-feedback/assets/wpfui-edgecase-2026-06-30/main-window.png",
                "docfx/agent-feedback/assets/wpfui-edgecase-2026-06-30/mcp-hero-screenshot.png",
                "docfx/agent-feedback/index.md",
                "docfx/agent-feedback/toc.yml",
                "docfx/zh-tw/agent-feedback/2026-03-17-agent-feedback-63-tool-e2e-validation.md",
                "docfx/zh-tw/agent-feedback/2026-06-24-agent-feedback-security-deep-scan.md",
                "docfx/zh-tw/agent-feedback/2026-06-29-agent-feedback-mahapps-real-project-e2e.md",
                "docfx/zh-tw/agent-feedback/2026-06-29-agent-feedback-materialdesign-real-project-e2e.md",
                "docfx/zh-tw/agent-feedback/2026-06-30-agent-feedback-handycontrol-real-project-e2e.md",
                "docfx/zh-tw/agent-feedback/2026-06-30-agent-feedback-wpfui-edgecase-e2e.md",
                "docfx/zh-tw/agent-feedback/index.md",
            ],
            "only the approved feedback reports and compact entry pages should be tracked");
        trackedFiles.Should().NotContain(path => path.Contains("template", StringComparison.OrdinalIgnoreCase));
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
