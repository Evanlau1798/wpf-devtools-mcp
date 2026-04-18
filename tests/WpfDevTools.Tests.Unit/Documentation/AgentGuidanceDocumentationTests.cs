using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class AgentGuidanceDocumentationTests
{
    [Theory]
    [InlineData("docfx/guides/common-workflows.md", "## Inspect a visual subtree")]
    [InlineData("docfx/guides/common-workflows.md", "## Analyze dependency property precedence")]
    [InlineData("docfx/guides/common-workflows.md", "## Safe interaction validation")]
    [InlineData("docfx/guides/common-workflows.md", "## Mutation with snapshot rollback")]
    [InlineData("docfx/guides/common-workflows.md", "## Focus-sensitive multi-window workflow")]
    [InlineData("docfx/guides/common-workflows.md", "## Layout and performance triage")]
    [InlineData("docfx/zh-tw/guides/common-workflows.md", "## 檢查 visual subtree")]
    [InlineData("docfx/zh-tw/guides/common-workflows.md", "## 分析 dependency property 優先順序")]
    [InlineData("docfx/zh-tw/guides/common-workflows.md", "## 安全的互動驗證")]
    [InlineData("docfx/zh-tw/guides/common-workflows.md", "## 搭配 snapshot 的可回復 mutation 流程")]
    [InlineData("docfx/zh-tw/guides/common-workflows.md", "## 焦點敏感的多視窗工作流")]
    [InlineData("docfx/zh-tw/guides/common-workflows.md", "## Layout 與效能 triage")]
    public void CommonWorkflows_SessionScopedSections_ShouldExplicitlyStartWithConnect(string relativePath, string heading)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));
        var section = ExtractSection(content, heading);
        var normalizedSection = section.Replace("\r\n", "\n", StringComparison.Ordinal);

        normalizedSection.Should().StartWith($"{heading}\n\n1. `connect`",
            $"{relativePath} should make connect the first step for session-scoped workflows in {heading}");
    }

    [Fact]
    public void EventGuidance_ShouldPreferDrainEventsForReadback()
    {
        var serverInstructions = File.ReadAllText(GetRepoFilePath("src/WpfDevTools.Mcp.Server/ServerInstructions.cs"));
        var workflowPrompts = File.ReadAllText(GetRepoFilePath("src/WpfDevTools.Mcp.Server/McpPrompts/WorkflowPrompts.cs"));

        serverInstructions.Should().Contain("drain_events",
            "server instructions should guide agents toward the shared event buffer read path");
        serverInstructions.Should().NotContain("trace_routed_events(mode=\"get\")",
            "server instructions should not prefer the legacy trace_routed_events(mode=\"get\") readback workflow");

        workflowPrompts.Should().Contain("drain_events",
            "prompt guidance should steer agents to drain_events when reading buffered routed events");
        workflowPrompts.Should().NotContain("trace_routed_events(mode='get')",
            "prompt guidance should not keep the stale trace_routed_events(mode='get') readback step");
    }

    [Fact]
    public void ServerInstructions_ShouldDescribeStructuredRecoveryFields()
    {
        var serverInstructions = File.ReadAllText(GetRepoFilePath("src/WpfDevTools.Mcp.Server/ServerInstructions.cs"));

        serverInstructions.Should().Contain("suggestedAction",
            "the primary agent-facing instructions should advertise structured recovery hints");
        serverInstructions.Should().Contain("requiresReconnect",
            "the primary agent-facing instructions should describe reconnect guidance for stale pipe sessions");
        serverInstructions.Should().Contain("retryAfterSeconds",
            "the primary agent-facing instructions should mention rate-limit backoff fields");
    }

    [Theory]
    [InlineData("docfx/reference/error-model.md")]
    [InlineData("docfx/zh-tw/reference/error-model.md")]
    public void ErrorModelDocumentation_ShouldDescribeStructuredRecoveryFields(string relativePath)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain("suggestedAction",
            "error-model docs should describe the recovery hints returned by modern tool contracts");
        content.Should().Contain("requiresReconnect",
            "error-model docs should explain reconnect guidance for stale or timed-out pipe sessions");
        content.Should().Contain("retryAfterSeconds",
            "error-model docs should document rate-limit recovery fields for agents");
    }

    private static string ExtractSection(string content, string heading)
    {
        var startIndex = content.IndexOf(heading, StringComparison.Ordinal);
        startIndex.Should().BeGreaterThanOrEqualTo(0, $"expected heading {heading} to exist");

        var nextHeadingIndex = content.IndexOf("\n## ", startIndex + heading.Length, StringComparison.Ordinal);
        return nextHeadingIndex >= 0
            ? content.Substring(startIndex, nextHeadingIndex - startIndex)
            : content[startIndex..];
    }

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);
}