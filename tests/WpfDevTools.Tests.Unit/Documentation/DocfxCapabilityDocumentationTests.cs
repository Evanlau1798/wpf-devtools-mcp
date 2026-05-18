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
    [InlineData(
        "docfx/architecture/ipc.md",
        "event push from inspector to server",
        "buffered events are surfaced by explicit drain, polling, or piggyback fields")]
    [InlineData(
        "docfx/zh-tw/architecture/ipc.md",
        "inspector 可以主動推送 event",
        "buffered event 會透過 explicit drain、polling 或 piggyback 欄位呈現")]
    public void IpcArchitecturePages_ShouldNotClaimUnsolicitedInspectorEventPush(
        string relativePath,
        string forbiddenPushClaim,
        string expectedBufferedSemantics)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().NotContain(forbiddenPushClaim,
            "the shipping STDIO/named-pipe workflow does not deliver unsolicited inspector event pushes");
        content.Should().Contain(expectedBufferedSemantics);
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
    [InlineData("docfx/reference/tools/process-and-connection.md")]
    [InlineData("docfx/zh-tw/reference/tools/process-and-connection.md")]
    public void ProcessReferencePages_ShouldDocumentDirectConnectOverrideExamples(string relativePath)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain("connect(windowFilter='all')",
            "process reference pages should show the direct auto-discovery override for hidden or background targets");
        content.Should().Contain("connect(selectionStrategy='largest_working_set', windowFilter='all')",
            "process reference pages should show the explicit multi-process auto-selection override instead of only describing it abstractly");
    }

    [Theory]
    [InlineData("docfx/reference/tools/process-and-connection.md", "After connect succeeds, prefer get_ui_summary, get_element_snapshot, or get_form_summary before any tree-heavy follow-up.")]
    [InlineData("docfx/zh-tw/reference/tools/process-and-connection.md", "connect 成功後，優先使用 `get_ui_summary`、`get_element_snapshot` 或 `get_form_summary` 建立 scene-first 上下文，再決定是否真的需要 tree-heavy follow-up。")] 
    public void ProcessReferencePages_ShouldExplicitlyPreferSceneFirstFollowUpsAfterConnect(string relativePath, string expectedSnippet)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain(expectedSnippet,
            "process reference pages should explicitly steer connect-success follow-ups toward scene-first context before tree expansion");
    }

    [Theory]
    [InlineData("docfx/reference/tools/index.md", "3. `get_ui_summary`, `get_element_snapshot`, or `get_form_summary`")]
    [InlineData("docfx/zh-tw/reference/tools/index.md", "3. `get_ui_summary`、`get_element_snapshot` 或 `get_form_summary`")]
    public void ToolOverviewPages_ShouldListSceneFirstTriadInRecommendedStepThree(
        string relativePath,
        string expectedStep)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain(expectedStep,
            "tool overview pages should present get_ui_summary, get_element_snapshot, and get_form_summary as the same scene-first entry step");
    }

    [Theory]
    [InlineData("docfx/reference/tools/index.md", "inspect background or foreground-only windows explicitly", "connect(windowFilter='all')")]
    [InlineData("docfx/zh-tw/reference/tools/index.md", "需要明確查看背景或前景視窗", "connect(windowFilter='all')")]
    public void ToolOverviewPages_ShouldNotTreatBroaderAutoDiscoveryAsGetProcessesDefaultUseCase(
        string relativePath,
        string staleGuidance,
        string directConnectOverride)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().NotContain(staleGuidance,
            "overview pages should not steer clients toward get_processes when they only need to widen connect auto-discovery scope");
        content.Should().Contain(directConnectOverride,
            "overview pages should point clients at the direct connect override when broader auto-discovery is the real goal");
    }

    [Theory]
    [InlineData("docfx/reference/tools/tree-and-xaml.md")]
    [InlineData("docfx/zh-tw/reference/tools/tree-and-xaml.md")]
    public void TreeReferencePages_ShouldDocumentGetWindows(string relativePath)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain("get_windows");
        content.Should().Contain("elementId");
    }

    [Theory]
    [InlineData("docfx/reference/tools/tree-and-xaml.md")]
    [InlineData("docfx/zh-tw/reference/tools/tree-and-xaml.md")]
    public void TreeReferencePages_ShouldDocumentFindElementsTraversalCaps(string relativePath)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain("find_elements");
        content.Should().Contain("maxTraversalNodes");
        content.Should().Contain("traversalNodeCount");
        content.Should().Contain("traversalTruncated");
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
    [InlineData("docfx/reference/tools/binding-and-dp.md", "cleanupIncomplete", "uncapped live read internally", "matching live event that exceeds the caller-visible result cap remains buffered", "errorData.replayPreserved", "errorData.bufferedReplayEventCount")]
    [InlineData("docfx/zh-tw/reference/tools/binding-and-dp.md", "cleanupIncomplete", "uncapped live read", "超出 caller-visible result cap 的 matching live event，都會保留到下一次 `drain_events` 呼叫", "errorData.replayPreserved", "errorData.bufferedReplayEventCount")]
    public void BindingReferencePages_ShouldDocumentDrainCleanupDiagnosticsAndReplaySubsetRetention(
        string relativePath,
        string cleanupKeyword,
        string uncappedKeyword,
        string retentionKeyword,
        string replayPreservedKeyword,
        string replayCountKeyword)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain(cleanupKeyword);
        content.Should().Contain("cleanupFailureMessage");
        content.Should().Contain("cleanupFailureType");
        content.Should().Contain(uncappedKeyword);
        content.Should().Contain(retentionKeyword);
        content.Should().Contain(replayPreservedKeyword);
        content.Should().Contain(replayCountKeyword);
        content.Should().Contain("drain_events");
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

        english.Should().Contain("claude-code.txt");
        english.Should().Contain("/mcp__wpf-devtools__");
        english.Should().Contain("@wpf-devtools:");
        traditionalChinese.Should().Contain("claude-code.txt");
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

    [Theory]
    [InlineData("docfx/reference/tools/index.md", "structuredContent", "outputSchema", "Claude")]
    [InlineData("docfx/zh-tw/reference/tools/index.md", "structuredContent", "outputSchema", "Claude")]
    public void ToolOverviewPages_ShouldExplainStructuredContentCompatibilityContract(
        string relativePath,
        string structuredContentKeyword,
        string outputSchemaKeyword,
        string claudeKeyword)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain(structuredContentKeyword);
        content.Should().Contain(outputSchemaKeyword,
            "tool overview pages should explain that tools/list advertises outputSchema while structuredContent remains canonical");
        content.Should().Contain(claudeKeyword,
            "tool overview pages should describe the current Claude compatibility expectation for structured-output metadata");
    }

    [Theory]
    [InlineData("docfx/reference/tools/index.md")]
    [InlineData("docfx/zh-tw/reference/tools/index.md")]
    public void ToolOverviewPages_ShouldPointToMachineReadableResponseContractResource(string relativePath)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain("wpf://contracts/response",
            "tool overview pages should point clients at the machine-readable response contract resource for stable payload details beyond SDK outputSchema");
        content.Should().Contain("JSON",
            "tool overview pages should clarify that the fallback contract surface is machine-readable JSON rather than prose alone");
    }

    [Fact]
    public void ToolOverviewPages_ShouldDescribeTextFallbackAsHighSignalSummary()
    {
        var english = File.ReadAllText(GetRepoFilePath("docfx/reference/tools/index.md"));
        var traditionalChinese = File.ReadAllText(GetRepoFilePath("docfx/zh-tw/reference/tools/index.md"));

        english.Should().Contain("high-signal top-level scalar fields");
        english.Should().Contain("collection counts");
        english.Should().Contain("WPFDEVTOOLS_TEXT_FALLBACK_MODE=full");
        traditionalChinese.Should().Contain("高訊號");
        traditionalChinese.Should().Contain("集合計數");
        traditionalChinese.Should().Contain("WPFDEVTOOLS_TEXT_FALLBACK_MODE=full");
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
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);
}
