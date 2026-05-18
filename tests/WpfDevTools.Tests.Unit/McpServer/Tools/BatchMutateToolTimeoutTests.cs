using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public sealed class BatchMutateToolTimeoutTests
{
    [Fact]
    public async Task ExecuteAsync_WhenMutationReturnsTimeoutPayloadAfterSnapshot_ShouldEscalateBatchTimeoutRecovery()
    {
        var executedProperties = new List<string?>();
        var sessionManager = new SessionManager();
        var tool = new BatchMutateTool(
            sessionManager,
            (_, args, _) =>
            {
                var propertyName = args.GetProperty("propertyName").GetString();
                executedProperties.Add(propertyName);

                return Task.FromResult<object>(propertyName == "Age"
                    ? new
                    {
                        success = false,
                        error = "Timed out waiting for set_dp_value to complete.",
                        errorCode = "Timeout",
                        stateAfterTimeoutUnknown = true
                    }
                    : new { success = true, propertyName });
            },
            (args, _) => Task.FromResult(SaveStoredSnapshotResult(sessionManager, args, "snapshot_batch_timeout_payload")),
            null);

        var result = JsonSerializer.SerializeToElement(await tool.ExecuteAsync(
            ToJsonElement(new
            {
                processId = 12345,
                captureSnapshot = new
                {
                    viewModelPropertyNames = new[] { "Name", "Age", "Title" }
                },
                mutations = new object[]
                {
                    new { tool = "modify_viewmodel", args = new { propertyName = "Name", value = "Updated" } },
                    new { tool = "set_dp_value", args = new { propertyName = "Age", value = 32 } },
                    new { tool = "modify_viewmodel", args = new { propertyName = "Title", value = "Skipped" } }
                }
            }),
            CancellationToken.None));

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("errorCode").GetString().Should().Be("Timeout");
        result.GetProperty("executedMutationCount").GetInt32().Should().Be(2);
        result.GetProperty("successfulMutationCount").GetInt32().Should().Be(1);
        result.GetProperty("failedMutationCount").GetInt32().Should().Be(1);
        result.GetProperty("skippedMutationCount").GetInt32().Should().Be(1);
        result.GetProperty("stateAfterTimeoutUnknown").GetBoolean().Should().BeTrue();
        result.GetProperty("requiresReconnect").GetBoolean().Should().BeTrue();
        result.GetProperty("rollback").GetProperty("available").GetBoolean().Should().BeTrue();
        result.GetProperty("recovery").GetProperty("tool").GetString().Should().Be("restore_state_snapshot");
        result.GetProperty("recovery").GetProperty("params").GetProperty("snapshotId").GetString().Should().Be("snapshot_batch_timeout_payload");
        result.GetProperty("mutations")[1].GetProperty("stateAfterTimeoutUnknown").GetBoolean().Should().BeTrue();
        result.GetProperty("mutations")[2].GetProperty("skipped").GetBoolean().Should().BeTrue();
        executedProperties.Should().Equal("Name", "Age");
    }

    [Fact]
    public async Task ExecuteAsync_WhenStateDiffIsCanceledAfterSnapshot_ShouldReturnPartialStateAndRollbackRecovery()
    {
        using var cancellation = new CancellationTokenSource();
        var sessionManager = new SessionManager();
        var tool = new BatchMutateTool(
            sessionManager,
            (_, _, _) => Task.FromResult<object>(new { success = true }),
            (args, _) => Task.FromResult(SaveStoredSnapshotResult(sessionManager, args, "snapshot_batch_diff_cancel")),
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
