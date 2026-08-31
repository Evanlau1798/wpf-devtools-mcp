using FluentAssertions;
using WpfDevTools.Mcp.Server.McpResources;
using WpfDevTools.Mcp.Server.McpTools;
using WpfDevTools.Mcp.Server.Navigation;
using WpfDevTools.Mcp.Server.Schema;

namespace WpfDevTools.Tests.Unit.McpServer;

public partial class ToolCallHelperTests
{
    [Fact]
    public void ResponseContract_ShouldDescribeMergedNextStepPrecedence()
    {
        using var document = System.Text.Json.JsonDocument.Parse(CapabilityResources.GetResponseContract());

        document.RootElement.GetProperty("nextSteps").GetProperty("derivedFrom").GetString()
            .Should().Contain("tool-specific entries")
            .And.Contain("unique navigation.recommended entries");
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_ShouldPreserveToolRecoveryBeforeUniquePlannerSteps()
    {
        var duplicateParams = ToolCallHelper.BuildJsonArgs(("snapshotId", "snapshot_123"))!.Value;
        var distinctParams = ToolCallHelper.BuildJsonArgs(("snapshotId", "snapshot_456"))!.Value;
        var registry = new ToolNavigationRegistry();
        registry.Register("known_tool", _ => ToolNavigationEnvelope.FromRecommended(
        [
            new ToolNextStep(
                "restore_state_snapshot",
                duplicateParams,
                "Planner retry.",
                ToolNextStepKind.Action,
                1),
            new ToolNextStep(
                "restore_state_snapshot",
                distinctParams,
                "Planner recovery for another snapshot.",
                ToolNextStepKind.Action,
                2)
        ]));
        using var scope = ToolCallHelper.BeginTestScope(
            navigationPlanner: new ToolNavigationPlanner(registry));

        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(new
            {
                success = false,
                nextSteps = new[]
                {
                    new
                    {
                        tool = "restore_state_snapshot",
                        @params = new { snapshotId = "snapshot_123" },
                        reason = "Retry the exact interrupted snapshot after reconnecting."
                    }
                }
            }),
            null,
            CancellationToken.None,
            toolName: "known_tool");

        var payload = result.StructuredContent!.Value;
        var nextSteps = payload.GetProperty("nextSteps");
        nextSteps.GetArrayLength().Should().Be(2);
        nextSteps[0].GetProperty("reason").GetString().Should().StartWith("Retry the exact interrupted snapshot");
        nextSteps[1].GetProperty("params").GetProperty("snapshotId").GetString().Should().Be("snapshot_456");

        var recommended = payload.GetProperty("navigation").GetProperty("recommended");
        recommended.GetArrayLength().Should().Be(2);
        recommended[0].GetProperty("reason").GetString().Should().Be("Planner retry.");
    }
}
