using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class DocfxCapabilityDocumentationTests
{
    [Fact]
    public void ToolOverviewPages_ShouldReflectCurrentToolCount()
    {
        var toolCount = Directory
            .EnumerateFiles(GetRepoFilePath("src/WpfDevTools.Mcp.Server/McpTools"), "*.cs", SearchOption.TopDirectoryOnly)
            .Select(File.ReadAllText)
            .Sum(content => CountOccurrences(content, "[McpServerTool("));

        File.ReadAllText(GetRepoFilePath("docfx/reference/tools/index.md"))
            .Should().Contain($"{toolCount} tools");
        File.ReadAllText(GetRepoFilePath("docfx/zh-tw/reference/tools/index.md"))
            .Should().Contain($"{toolCount} 個工具");
    }

    [Theory]
    [InlineData("docfx/reference/tools/process-and-connection.md")]
    [InlineData("docfx/zh-tw/reference/tools/process-and-connection.md")]
    public void ProcessReferencePages_ShouldDocumentActiveProcessWorkflow(string relativePath)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain("get_processes");
        content.Should().Contain("select_active_process");
        content.Should().Contain("get_active_process");
        content.Should().Contain("connect");
        content.Should().Contain("ping");
    }

    [Theory]
    [InlineData("docfx/reference/tools/process-and-connection.md", "Exception-only discovery path")]
    [InlineData("docfx/zh-tw/reference/tools/process-and-connection.md", "例外情境")]
    public void ProcessReferencePages_ShouldMarkListThenConnectSequenceAsExceptionPath(
        string relativePath,
        string marker)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain("get_processes(windowFilter) -> connect(processId)");
        content.Should().Contain(marker,
            "list-then-connect examples should be framed as exception-only disambiguation flows");
    }

    [Theory]
    [InlineData("docfx/reference/tools/interaction-events-layout.md")]
    [InlineData("docfx/zh-tw/reference/tools/interaction-events-layout.md")]
    public void InteractionReferencePages_ShouldDocumentFocusAndStateTools(string relativePath)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain("get_focus_state");
        content.Should().Contain("focus_element");
        content.Should().Contain("capture_state_snapshot");
        content.Should().Contain("restore_state_snapshot");
    }

    [Theory]
    [InlineData("docfx/guides/common-workflows.md")]
    [InlineData("docfx/zh-tw/guides/common-workflows.md")]
    public void CommonWorkflowPages_ShouldDescribeSnapshotAndFocusSafetyPatterns(string relativePath)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain("capture_state_snapshot");
        content.Should().Contain("restore_state_snapshot");
        content.Should().Contain("get_focus_state");
        content.Should().Contain("focus_element");
    }

    [Theory]
    [InlineData("docfx/guides/troubleshooting.md", "elevated", "Access denied", "SDK mode", "project-scoped")]
    [InlineData("docfx/zh-tw/guides/troubleshooting.md", "系統管理員", "Access denied", "SDK mode", "project-scoped")]
    public void TroubleshootingPages_ShouldCoverElevationAndRegistrationConstraints(
        string relativePath,
        string elevationKeyword,
        string accessDeniedKeyword,
        string sdkModeKeyword,
        string projectScopeKeyword)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().ContainEquivalentOf(elevationKeyword);
        content.Should().ContainEquivalentOf(accessDeniedKeyword);
        content.Should().ContainEquivalentOf(sdkModeKeyword);
        content.Should().ContainEquivalentOf(projectScopeKeyword);
    }

    [Fact]
    public void ClaudeQuickstartPages_ShouldDescribeProjectArtifactAndDiscoveryEntryPoints()
    {
        var english = File.ReadAllText(GetRepoFilePath("docfx/quickstart/claude-code.md"));
        var traditionalChinese = File.ReadAllText(GetRepoFilePath("docfx/zh-tw/quickstart/claude-code.md"));

        english.Should().Contain("claude-code.project.mcp.json");
        english.Should().Contain("/mcp__wpf-devtools__");
        english.Should().Contain("@wpf-devtools:");
        traditionalChinese.Should().Contain("claude-code.project.mcp.json");
        traditionalChinese.Should().Contain("/mcp__wpf-devtools__");
        traditionalChinese.Should().Contain("@wpf-devtools:");
    }

    [Theory]
    [InlineData("docfx/quickstart/openai-codex.md", "administrator", "elevated")]
    [InlineData("docfx/zh-tw/quickstart/openai-codex.md", "系統管理員", "elevated")]
    [InlineData("docfx/guides/ai-agent-guide.md", "capture_state_snapshot", "diagnosticKind")]
    [InlineData("docfx/zh-tw/guides/ai-agent-guide.md", "capture_state_snapshot", "diagnosticKind")]
    public void AgentFacingDocs_ShouldDescribeCurrentRuntimeContracts(
        string relativePath,
        string firstKeyword,
        string secondKeyword)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain(firstKeyword);
        content.Should().Contain(secondKeyword);
    }

    [Theory]
    [InlineData("docfx/guides/ai-agent-guide.md")]
    [InlineData("docfx/zh-tw/guides/ai-agent-guide.md")]
    public void AgentGuides_ShouldRecommendConnectAutoDiscoveryAndSceneDiagnostics(string relativePath)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain("connect()");
        content.Should().Contain("windowFilter");
        content.Should().Contain("get_ui_summary");
        content.Should().Contain("get_element_snapshot");
        content.Should().Contain("get_form_summary");
        content.Should().Contain("get_state_diff");
    }

    private static int CountOccurrences(string content, string value)
    {
        var count = 0;
        var startIndex = 0;

        while ((startIndex = content.IndexOf(value, startIndex, StringComparison.Ordinal)) >= 0)
        {
            count++;
            startIndex += value.Length;
        }

        return count;
    }

    private static string GetRepoFilePath(string relativePath)
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relativePath));
}
