using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public sealed class BatchMutateToolTimeoutTests
{
    [Fact]
    public async Task ExecuteAsync_WhenStateDiffIsCanceledAfterSnapshot_ShouldReturnPartialStateAndRollbackRecovery()
    {
        using var cancellation = new CancellationTokenSource();
        var tool = new BatchMutateTool(
            new SessionManager(),
            (_, _, _) => Task.FromResult<object>(new { success = true }),
            (_, _) => Task.FromResult<object>(new
            {
                success = true,
                snapshotId = "snapshot_batch_diff_cancel"
            }),
            (_, token) =>
            {
                cancellation.Cancel();
                throw new OperationCanceledException(token);
            });

        var result = JsonSerializer.SerializeToElement(await tool.ExecuteAsync(
            ToJsonElement(new
            {
                processId = 12345,
                captureSnapshot = new
                {
                    propertyNames = new[] { "Text" }
                },
                includeDiff = true,
                mutations = new object[]
                {
                    new { tool = "set_dp_value", args = new { propertyName = "Text", value = "Updated" } }
                }
            }),
            cancellation.Token));

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("errorCode").GetString().Should().Be("Timeout");
        result.GetProperty("executedMutationCount").GetInt32().Should().Be(1);
        result.GetProperty("successfulMutationCount").GetInt32().Should().Be(1);
        result.GetProperty("failedMutationCount").GetInt32().Should().Be(0);
        result.GetProperty("stateAfterTimeoutUnknown").GetBoolean().Should().BeTrue();
        result.GetProperty("rollback").GetProperty("available").GetBoolean().Should().BeTrue();
        result.GetProperty("recovery").GetProperty("tool").GetString().Should().Be("restore_state_snapshot");
        result.GetProperty("recovery").GetProperty("params").GetProperty("snapshotId").GetString().Should().Be("snapshot_batch_diff_cancel");
    }
}
