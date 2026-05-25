using System.Reflection;
using System.ComponentModel;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.McpTools;
using WpfDevTools.Mcp.Server.McpPrompts;
using WpfDevTools.Mcp.Server.McpResources;
using Xunit;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class ConnectAllowlistDocumentationTests
{
    [Theory]
    [InlineData("README.md", "## Typical MCP workflow")]
    [InlineData("docfx/quickstart/index.md", "## Step 5: Verify the first session")]
    [InlineData("docfx/quickstart/ai-agent-clients.md", "## First verification flow")]
    [InlineData("docfx/quickstart/claude-code.md", "## 5. First useful prompt")]
    [InlineData("docfx/quickstart/openai-codex.md", "## 5. First useful prompt")]
    [InlineData("docfx/quickstart/claude-desktop.md", "## 3. First prompt")]
    [InlineData("docfx/quickstart/cursor-vscode.md", "## 3. First useful workflow")]
    [InlineData("docfx/zh-tw/quickstart/index.md", "## Step 5")]
    [InlineData("docfx/zh-tw/quickstart/ai-agent-clients.md", "## 第一次驗證流程")]
    [InlineData("docfx/zh-tw/quickstart/claude-code.md", "## 5. 第一個實用 prompt")]
    [InlineData("docfx/zh-tw/quickstart/openai-codex.md", "## 5. 第一個實用 prompt")]
    [InlineData("docfx/zh-tw/quickstart/claude-desktop.md", "## 3. 第一個 prompt")]
    [InlineData("docfx/zh-tw/quickstart/cursor-vscode.md", "## 3. 第一個實用流程")]
    [InlineData("docfx/reference/tools/index.md", "1. `connect()`")]
    [InlineData("docfx/reference/tools/process-and-connection.md", "connect()` auto-discovers")]
    [InlineData("docfx/zh-tw/reference/tools/index.md", "1. `connect()`")]
    [InlineData("docfx/zh-tw/reference/tools/process-and-connection.md", "connect()`")]
    public void FirstRunConnectDocs_ShouldStateMcpTargetAllowlistBeforeSuccessWorkflow(string relativePath, string workflowMarker)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain("WPFDEVTOOLS_MCP_ALLOWED_TARGETS",
            $"{relativePath} should tell first-run users that connect() requires the MCP target allowlist before it can succeed");
        content.Should().Contain("exact local absolute executable path",
            $"{relativePath} should require exact absolute paths instead of path fragments or relative paths");
        content.IndexOf("WPFDEVTOOLS_MCP_ALLOWED_TARGETS", StringComparison.Ordinal)
            .Should().BeLessThan(content.IndexOf(workflowMarker, StringComparison.Ordinal),
                $"{relativePath} should explain the allowlist prerequisite before the successful connect() workflow");
        content.Should().Contain("fail closed",
            $"{relativePath} should describe the security behavior when the allowlist is unset or malformed");
    }

    [Theory]
    [InlineData("docfx/quickstart/index.md", "`connect()` succeeds immediately when there is only one visible WPF target")]
    [InlineData("docfx/zh-tw/quickstart/index.md", "`connect()` 在只有一個可見 WPF target 時立即成功")]
    public void QuickstartHealthyFirstRunSigns_ShouldNotOmitAllowlistPrerequisite(string relativePath, string staleClaim)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().NotContain(staleClaim,
            $"{relativePath} should not imply that visibility alone is enough for connect() to succeed");
        content.Should().Contain("WPFDEVTOOLS_MCP_ALLOWED_TARGETS",
            $"{relativePath} should pair first-run connect success with the required MCP target allowlist");
    }

    [Theory]
    [InlineData("docfx/reference/tools/process-and-connection.md")]
    [InlineData("docfx/zh-tw/reference/tools/process-and-connection.md")]
    public void ProcessToolReference_ShouldDescribeAllowlistScopedDiscoveryAndRedaction(string relativePath)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain("allowlisted targets",
            $"{relativePath} should not imply get_processes is unrestricted process metadata discovery");
        content.Should().Contain("redactedTargetCount",
            $"{relativePath} should document aggregate redaction for targets blocked by policy");
        content.Should().Contain("denied target",
            $"{relativePath} should make blocked-target metadata handling explicit");
    }

    [Fact]
    public void ServerInstructions_ShouldStateMcpTargetAllowlistInsideMandatoryWorkflowBeforeConnect()
    {
        var workflow = ExtractSection(
            ServerInstructions.Value,
            "=== MANDATORY WORKFLOW ===",
            "=== PARAMETER CONVENTIONS ===");

        AssertAllowlistBeforeConnect(workflow, "ServerInstructions mandatory workflow");
        workflow.Should().NotContain("1. connect() -> try auto-discovery against visible WPF apps",
            "mandatory workflow should not present visibility-only connect success before target authorization");
    }

    [Fact]
    public void CapabilityResource_ShouldStateMcpTargetAllowlistBeforeRecommendedConnectWorkflow()
    {
        var workflow = ExtractSection(
            CapabilityResources.GetCapabilities(),
            "## Recommended workflow shape",
            "## Response contract notes");

        AssertAllowlistBeforeConnect(workflow, "wpf://capabilities recommended workflow");
        workflow.Should().NotContain("Start with `connect()` and let auto-discovery pick the single visible WPF target when possible",
            "capability guidance should not imply visibility alone is enough for connect success");
    }

    [Fact]
    public void WorkflowPrompts_WithConnectSteps_ShouldStateMcpTargetAllowlistBeforeConnectStep()
    {
        var promptMethods = typeof(WorkflowPrompts)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.ReturnType == typeof(string) && method.GetParameters().Length == 0);

        foreach (var method in promptMethods)
        {
            var prompt = (string)method.Invoke(null, null)!;
            if (!prompt.Contains("connect", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var workflow = ExtractSection(prompt, "Recommended workflow:", endMarker: null);
            AssertAllowlistBeforeConnect(workflow, $"WorkflowPrompts.{method.Name}");
            workflow.Should().NotContain("Call connect() first; let the server auto-discover the target when there is only one visible WPF app",
                $"{method.Name} should not imply visibility alone is enough for connect success");
        }
    }

    [Fact]
    public void ConnectToolMetadata_ShouldStateMcpTargetAllowlistBeforeAutoDiscoveryGuidance()
    {
        var method = typeof(ProcessMcpTools).GetMethod(nameof(ProcessMcpTools.Connect))!;
        var description = method.GetCustomAttribute<DescriptionAttribute>()!.Description;

        AssertAllowlistBeforeConnect(description, "connect tool description");
        description.Should().Contain("AUTO-DISCOVERY");
        description.Should().Contain("allowlisted");
        description.Should().NotContain("If processId is omitted, connect auto-discovers the target when exactly one WPF process is running",
            "connect tool metadata should not imply process cardinality alone is enough for success");
        description.Should().NotContain("Omit processId to auto-connect when exactly one WPF process is available",
            "connect tool metadata should not imply visibility/cardinality alone is enough for success");

        var processIdDescription = method.GetParameters()
            .Single(parameter => parameter.Name == "processId")
            .GetCustomAttribute<DescriptionAttribute>()!
            .Description;

        processIdDescription.Should().Contain("WPFDEVTOOLS_MCP_ALLOWED_TARGETS");
        processIdDescription.Should().Contain("exact local absolute executable path");
        processIdDescription.Should().Contain("fail closed");
        processIdDescription.Should().Contain("allowlisted");
    }

    [Theory]
    [InlineData("docfx/guides/ai-agent-guide.md", "## Prompt patterns that work well", "## Anti-patterns")]
    [InlineData("docfx/zh-tw/guides/ai-agent-guide.md", "## 容易成功的提示模式", "## 常見反模式")]
    public void AiAgentGuidePromptPatterns_ShouldStateMcpTargetAllowlistInsideCopyReadySection(
        string relativePath,
        string startMarker,
        string endMarker)
    {
        var section = ExtractSection(File.ReadAllText(GetRepoFilePath(relativePath)), startMarker, endMarker);

        AssertAllowlistBeforeConnect(section, $"{relativePath} prompt patterns");
    }

    [Theory]
    [InlineData("docfx/guides/ai-agent-guide.md", "## Golden sequence for automation")]
    [InlineData("docfx/zh-tw/guides/ai-agent-guide.md", "## 自動化的黃金順序")]
    public void AiAgentGuideGoldenSequence_ShouldStateMcpTargetAllowlistBeforeConnectStep(
        string relativePath,
        string startMarker)
    {
        var section = ExtractSection(File.ReadAllText(GetRepoFilePath(relativePath)), startMarker, endMarker: null);

        AssertAllowlistBeforeConnect(section, $"{relativePath} golden sequence");
    }

    [Fact]
    public void AllowlistGuidance_ShouldNotOmitLocalAbsoluteExecutablePathRequirement()
    {
        string[] relativePaths =
        [
            "docfx/guides/ai-agent-guide.md",
            "docfx/zh-tw/guides/ai-agent-guide.md",
            "src/WpfDevTools.Mcp.Server/Tools/ConnectTool.AutoDiscovery.cs"
        ];

        var violations = relativePaths
            .SelectMany(relativePath => File.ReadLines(GetRepoFilePath(relativePath))
                .Select((line, index) => new
                {
                    RelativePath = relativePath,
                    LineNumber = index + 1,
                    Text = line
                }))
            .Where(entry => entry.Text.Contains("exact absolute path", StringComparison.OrdinalIgnoreCase)
                || entry.Text.Contains("exact reviewed executable path", StringComparison.OrdinalIgnoreCase))
            .Select(entry => $"{entry.RelativePath}:{entry.LineNumber}: {entry.Text.Trim()}")
            .ToArray();

        violations.Should().BeEmpty(
            "allowlist-facing guidance should consistently require exact local absolute executable paths");
    }

    private static void AssertAllowlistBeforeConnect(string content, string context)
    {
        content.Should().Contain("WPFDEVTOOLS_MCP_ALLOWED_TARGETS",
            $"{context} should mention the target allowlist before a successful connect step");
        content.Should().Contain("exact local absolute executable path",
            $"{context} should specify the exact executable path requirement");
        content.Should().Contain("fail closed",
            $"{context} should document unset or malformed allowlist behavior");

        content.IndexOf("WPFDEVTOOLS_MCP_ALLOWED_TARGETS", StringComparison.Ordinal)
            .Should().BeLessThan(content.IndexOf("connect(", StringComparison.OrdinalIgnoreCase),
                $"{context} should put the allowlist precondition before the first connect instruction");
    }

    private static string ExtractSection(string content, string startMarker, string? endMarker)
    {
        var start = content.IndexOf(startMarker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"content should include '{startMarker}'");

        if (endMarker is null)
        {
            return content[start..];
        }

        var end = content.IndexOf(endMarker, start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start, $"content should include '{endMarker}' after '{startMarker}'");
        return content[start..end];
    }

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);
}
