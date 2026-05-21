using FluentAssertions;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Tests.Unit.McpServer;

[Collection("ToolCallHelperState")]
public sealed class ToolNavigationPlannerBatchMutationTests : IDisposable
{
    private readonly IDisposable _toolCallHelperScope = ToolCallHelper.BeginTestScope();

    public void Dispose()
    {
        _toolCallHelperScope.Dispose();
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WithBatchMutationSnapshotWithoutDiff_ShouldRecommendDiffBeforeRollback()
    {
        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(new
            {
                success = true,
                snapshotId = "snapshot_123"
            }),
            ToolCallHelper.BuildJsonArgs(("processId", 12345), ("elementId", "NameTextBox")),
            CancellationToken.None,
            toolName: "batch_mutate");

        var recommendedTools = result.StructuredContent!.Value
            .GetProperty("navigation")
            .GetProperty("recommended")
            .EnumerateArray()
            .Select(step => step.GetProperty("tool").GetString())
            .ToArray();

        recommendedTools.Should().StartWith("get_state_diff");
        recommendedTools.Should().ContainInOrder("get_state_diff", "restore_state_snapshot");
    }
}
