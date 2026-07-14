using FluentAssertions;
using System.Reflection;
using ModelContextProtocol.Server;
using WpfDevTools.Mcp.Server.McpTools;
using Xunit;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class DocfxCapabilityDocumentationTests
{
    [Fact]
    public void ToolOverviewPages_ShouldReflectCurrentToolCount()
    {
        var toolCount = GetSourceRegisteredToolNames().Length;

        File.ReadAllText(GetRepoFilePath("docfx/reference/tools/index.md"))
            .Should().Contain($"{toolCount} tools");
        File.ReadAllText(GetRepoFilePath("docfx/zh-tw/reference/tools/index.md"))
            .Should().Contain($"{toolCount} 個工具");
    }

    [Theory]
    [InlineData("docfx/reference/tools")]
    [InlineData("docfx/zh-tw/reference/tools")]
    public void ToolReferenceCategoryPages_ShouldCoverEverySourceRegisteredTool(string relativeDirectory)
    {
        var toolNames = GetSourceRegisteredToolNames();
        var categoryContent = Directory
            .EnumerateFiles(GetRepoFilePath(relativeDirectory), "*.md", SearchOption.TopDirectoryOnly)
            .Where(path => !string.Equals(Path.GetFileName(path), "index.md", StringComparison.OrdinalIgnoreCase))
            .Select(File.ReadAllText)
            .Aggregate(string.Empty, static (left, right) => left + "\n" + right);

        var missingTools = toolNames
            .Where(toolName => !categoryContent.Contains(toolName, StringComparison.Ordinal))
            .ToArray();

        missingTools.Should().BeEmpty(
            "every source-registered MCP tool should have name-level coverage in a dedicated DocFX reference category page, not only the overview index");
    }

    [Theory]
    [InlineData("docfx/reference/tools/scene-and-state.md")]
    [InlineData("docfx/zh-tw/reference/tools/scene-and-state.md")]
    public void SceneAndStateReferencePages_ShouldDocumentPrimarySceneAndStateToolsAsHeadings(
        string relativePath)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));
        string[] expectedTools =
        [
            "get_ui_summary",
            "get_form_summary",
            "get_element_snapshot",
            "diagnose_visibility",
            "get_interaction_readiness",
            "capture_state_snapshot",
            "batch_mutate",
            "get_state_diff",
            "restore_state_snapshot"
        ];

        foreach (var toolName in expectedTools)
        {
            content.Should().Contain($"## `{toolName}`",
                "the dedicated scene/state reference page should expose primary workflow tools as sections, not only incidental mentions");
        }
    }

    [Theory]
    [InlineData("docfx/reference/tools/scene-and-state.md")]
    [InlineData("docfx/zh-tw/reference/tools/scene-and-state.md")]
    public void SceneAndStateReferencePages_ShouldDocumentFormSummarySubmittabilityAsNestedSummaryFields(
        string relativePath)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain("summary.validationSubmittable");
        content.Should().Contain("summary.interactionSubmittable");
        content.Should().Contain("summary.isSubmittable");
        content.Should().NotContain("`validationSubmittable`, `interactionSubmittable`,");
    }

    [Theory]
    [InlineData("docfx/reference/tools/scene-and-state.md", "capture_state_snapshot")]
    [InlineData("docfx/reference/tools/scene-and-state.md", "restore_state_snapshot")]
    [InlineData("docfx/reference/tools/scene-and-state.md", "batch_mutate")]
    [InlineData("docfx/zh-tw/reference/tools/scene-and-state.md", "capture_state_snapshot")]
    [InlineData("docfx/zh-tw/reference/tools/scene-and-state.md", "restore_state_snapshot")]
    [InlineData("docfx/zh-tw/reference/tools/scene-and-state.md", "batch_mutate")]
    public void SceneAndStateReferencePages_ShouldDocumentDestructiveGateForStateMutationTools(
        string relativePath,
        string toolName)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));
        var section = ExtractMarkdownSection(content, $"## `{toolName}`");

        section.Should().Contain("WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS");
        section.Should().Contain("destructive");
    }

    [Theory]
    [InlineData("docfx/reference/tools/scene-and-state.md")]
    [InlineData("docfx/zh-tw/reference/tools/scene-and-state.md")]
    public void SceneAndStateReferencePages_ShouldScopeBatchMutateSnapshotExampleToMutatedElement(
        string relativePath)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));
        var section = ExtractMarkdownSection(content, "## `batch_mutate`");

        section.Should().Contain("\"captureSnapshot\": { \"elementId\": \"NameTextBox\", \"propertyNames\": [\"Text\"] }");
    }

    [Theory]
    [InlineData("docfx/reference/tools/scene-and-state.md", "bound `DependencyProperty`", "ViewModel source", "`viewModelPropertyNames`")]
    [InlineData("docfx/zh-tw/reference/tools/scene-and-state.md", "綁定的 `DependencyProperty`", "ViewModel source", "`viewModelPropertyNames`")]
    public void SceneAndStateReferencePages_ShouldWarnBoundDpRollbackNeedsViewModelSourceCapture(
        string relativePath,
        string boundDpPhrase,
        string viewModelSourcePhrase,
        string viewModelPropertyNamesPhrase)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain(boundDpPhrase);
        content.Should().Contain(viewModelSourcePhrase);
        content.Should().Contain(viewModelPropertyNamesPhrase);
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
    [InlineData("docfx/reference/tools/process-and-connection.md", "After connect succeeds, prefer get_ui_summary or get_form_summary before any tree-heavy follow-up; use get_element_snapshot(elementId) only after a concrete elementId is known.")]
    [InlineData("docfx/zh-tw/reference/tools/process-and-connection.md", "connect 成功後，優先使用 `get_ui_summary` 或 `get_form_summary` 建立 scene-first 上下文；只有在已取得具體 `elementId` 後，才呼叫 `get_element_snapshot(elementId)`。")]
    public void ProcessReferencePages_ShouldExplicitlyPreferSceneFirstFollowUpsAfterConnect(string relativePath, string expectedSnippet)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain(expectedSnippet,
            "process reference pages should explicitly steer connect-success follow-ups toward scene-first context before tree expansion");
    }

    [Theory]
    [InlineData("docfx/reference/tools/index.md", "3. `get_ui_summary` or `get_form_summary` for scene-first context")]
    [InlineData("docfx/zh-tw/reference/tools/index.md", "3. `get_ui_summary` 或 `get_form_summary` 作為 scene-first context")]
    public void ToolOverviewPages_ShouldListDirectlyExecutableSceneFirstStepThree(
        string relativePath,
        string expectedStep)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain(expectedStep,
            "tool overview pages should list only directly executable scene-first calls after connect succeeds");
    }

    [Fact]
    public void DocfxPages_ShouldQualifyElementSnapshotWithElementIdPrecondition()
    {
        var repoRoot = GetRepoFilePath(".");
        var violations = EnumerateDocfxContractMarkdownFiles()
            .SelectMany(path => File
                .ReadLines(path)
                .Select((line, index) => new
                {
                    Path = Path.GetRelativePath(repoRoot, path),
                    LineNumber = index + 1,
                    Text = line
                }))
            .Where(entry => entry.Text.Contains("get_element_snapshot", StringComparison.Ordinal)
                && !entry.Text.Contains("elementId", StringComparison.Ordinal))
            .Select(entry => $"{entry.Path}:{entry.LineNumber}: {entry.Text}")
            .ToArray();

        violations.Should().BeEmpty(
            "get_element_snapshot requires an elementId and every DocFX mention should describe that precondition");
    }

    [Fact]
    public void DocfxContractSweeps_ShouldExcludeAgentFeedbackReports()
    {
        var repoRoot = GetRepoFilePath(".");
        var sweptPaths = EnumerateDocfxContractMarkdownFiles()
            .Select(path => Path.GetRelativePath(repoRoot, path).Replace('\\', '/'))
            .ToArray();

        sweptPaths.Should().NotContain(path => path.Contains("/agent-feedback/", StringComparison.Ordinal),
            "agent feedback reports are versioned local reports and can change outside stable documentation contracts");
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
        content.Should().Contain("searchComplete");
        content.Should().Contain("TraversalBudgetExceededBeforeMatch");
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
    public void ToolOverviewPages_ShouldPointToMachineReadableContractResources(string relativePath)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain("wpf://contracts/response",
            "tool overview pages should point clients at the machine-readable response contract resource for stable payload details beyond SDK outputSchema");
        content.Should().Contain("wpf://contracts/tools",
            "tool overview pages should point clients at the canonical tool manifest for machine-readable tool surface details");
        content.Should().Contain("text-chunks",
            "tool overview pages should advertise the portable text contract reconstruction route");
        content.Should().Contain("base64",
            "tool overview pages should explain that the text route does not require binary decoding");
        content.Should().Contain("capability tag",
            "tool overview pages should describe a field that the canonical tool manifest actually publishes");
        content.Should().Contain("parameter metadata",
            "tool overview pages should describe a field that the canonical tool manifest actually publishes");
        content.Should().NotContain("discovery alias",
            "tool overview pages should not promise alias metadata that the canonical manifest does not publish");
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

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);

    private static string[] GetSourceRegisteredToolNames()
    {
        return typeof(ProcessMcpTools).Assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<McpServerToolTypeAttribute>() != null)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Select(method => method.GetCustomAttribute<McpServerToolAttribute>()?.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static string ExtractMarkdownSection(string content, string heading)
    {
        var start = content.IndexOf(heading, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);

        var nextHeading = content.IndexOf("\n## ", start + heading.Length, StringComparison.Ordinal);
        return nextHeading < 0 ? content[start..] : content[start..nextHeading];
    }

    private static IEnumerable<string> EnumerateDocfxContractMarkdownFiles()
    {
        var docfxRoot = GetRepoFilePath("docfx");
        return Directory
            .EnumerateFiles(docfxRoot, "*.md", SearchOption.AllDirectories)
            .Where(path => !IsAgentFeedbackPath(docfxRoot, path));
    }

    private static bool IsAgentFeedbackPath(string docfxRoot, string path)
    {
        var relativePath = Path.GetRelativePath(docfxRoot, path).Replace('\\', '/');
        return relativePath.StartsWith("agent-feedback/", StringComparison.Ordinal)
            || relativePath.StartsWith("zh-tw/agent-feedback/", StringComparison.Ordinal);
    }
}
