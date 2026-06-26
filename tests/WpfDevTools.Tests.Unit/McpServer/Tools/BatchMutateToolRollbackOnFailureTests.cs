using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public sealed class BatchMutateToolRollbackOnFailureTests
{
    [Fact]
    public async Task ExecuteAsync_WithRollbackOnFailure_ShouldRestoreCapturedSnapshotBeforeReturn()
    {
        const int processId = 12345;
        var sessionManager = new SessionManager();
        var executedTools = new List<string>();
        var tool = new BatchMutateTool(
            sessionManager,
            (toolName, args, _) =>
            {
                executedTools.Add(toolName);
                var propertyName = args.GetProperty("propertyName").GetString();
                return Task.FromResult<object>(propertyName switch
                {
                    "Name" => new { success = true, propertyName },
                    "Age" => new { success = false, error = "Setter failed.", errorCode = "OperationFailed" },
                    _ => throw new InvalidOperationException("Skipped mutation should not execute")
                });
            },
            (args, _) => Task.FromResult(SaveStoredSnapshotResult(
                sessionManager,
                args,
                "snapshot_batch_auto_rollback")),
            null);

        var result = JsonSerializer.SerializeToElement(await tool.ExecuteAsync(
            ToJsonElement(new
            {
                processId,
                rollbackOnFailure = true,
                captureSnapshot = new
                {
                    viewModelPropertyNames = new[] { "Name", "Age", "Title" }
                },
                mutations = new object[]
                {
                    new { tool = "modify_viewmodel", args = new { propertyName = "Name", value = "Updated" } },
                    new { tool = "modify_viewmodel", args = new { propertyName = "Age", value = 32 } },
                    new { tool = "modify_viewmodel", args = new { propertyName = "Title", value = "Skipped" } }
                }
            }),
            CancellationToken.None));

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("rollback").GetProperty("rollbackOnFailure").GetBoolean().Should().BeTrue();
        result.GetProperty("rollback").GetProperty("attempted").GetBoolean().Should().BeTrue();
        result.GetProperty("rollback").GetProperty("succeeded").GetBoolean().Should().BeTrue();
        result.GetProperty("rollback").GetProperty("result").GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("rollback").GetProperty("available").GetBoolean().Should().BeFalse();
        result.GetProperty("rollback").GetProperty("reason").GetString().Should().Contain("already applied");
        sessionManager.TryGetStateSnapshot(processId, "snapshot_batch_auto_rollback", out _)
            .Should().BeFalse("successful automatic rollback removes the rollback snapshot");
        executedTools.Should().Equal("modify_viewmodel", "modify_viewmodel");
    }
}
