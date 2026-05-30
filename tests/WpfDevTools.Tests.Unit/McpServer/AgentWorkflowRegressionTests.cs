using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.McpPrompts;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class AgentWorkflowRegressionTests
{
    [Fact]
    public void ServerInstructions_ButtonClickWorkflow_ShouldCloseSnapshotMutationDiffRestoreLoop()
    {
        var workflow = ExtractSection(
            ServerInstructions.Value,
            "Workflow 2 - Test Button Click:",
            "Workflow 3 - Inspect ViewModel:");

        AssertToolOrder(
            workflow,
            "connect",
            "capture_state_snapshot",
            "get_interaction_readiness",
            "click_element",
            "get_state_diff",
            "restore_state_snapshot");
    }

    [Fact]
    public void CommandOrClickPrompt_ShouldRestoreAfterStateDiffWhenSnapshotWasCaptured()
    {
        var prompt = WorkflowPrompts.DebugCommandOrClick();

        AssertToolOrder(
            prompt,
            "capture_state_snapshot",
            "get_interaction_readiness",
            "click_element",
            "get_state_diff",
            "restore_state_snapshot");
    }

    private static string ExtractSection(string content, string startMarker, string endMarker)
    {
        var start = content.IndexOf(startMarker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"{startMarker} should exist");

        var end = content.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start, $"{endMarker} should follow {startMarker}");

        return content[start..end];
    }

    private static void AssertToolOrder(string content, params string[] toolNames)
    {
        var previousIndex = -1;
        foreach (var toolName in toolNames)
        {
            var index = content.IndexOf(toolName, StringComparison.Ordinal);
            index.Should().BeGreaterThan(
                previousIndex,
                $"{toolName} should appear after the previous workflow step in the agent-facing sequence");
            previousIndex = index;
        }
    }
}
