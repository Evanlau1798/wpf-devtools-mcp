using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public sealed class BatchMutateToolCaptureSnapshotInferenceTests
{
    [Fact]
    public async Task ExecuteAsync_WithCaptureSnapshotTrue_ShouldInferDpSnapshotFromMutationArgs()
    {
        JsonElement? capturedSnapshotArgs = null;
        var executedTools = new List<string>();
        var tool = new BatchMutateTool(
            new SessionManager(),
            (toolName, args, _) =>
            {
                executedTools.Add(toolName);
                return Task.FromResult<object>(new
                {
                    success = true,
                    propertyName = args.GetProperty("propertyName").GetString()
                });
            },
            (args, _) =>
            {
                capturedSnapshotArgs = args.Clone();
                return Task.FromResult<object>(new
                {
                    success = true,
                    snapshotId = "snapshot_batch_inferred"
                });
            },
            (_, _) => Task.FromResult<object>(new
            {
                success = true,
                snapshotId = "snapshot_batch_inferred",
                trigger = "batch_mutate",
                propertyChanges = Array.Empty<object>(),
                viewModelChanges = Array.Empty<object>(),
                newBindingErrors = Array.Empty<object>(),
                resolvedBindingErrors = Array.Empty<object>(),
                validationChanges = Array.Empty<object>(),
                focusChange = (object?)null
            }));

        var result = JsonSerializer.SerializeToElement(await tool.ExecuteAsync(
            ToJsonElement(new
            {
                processId = 12345,
                captureSnapshot = true,
                includeDiff = true,
                mutations = new object[]
                {
                    new { tool = "set_dp_value", args = new { elementId = "Button_61", propertyName = "Tag", value = "batch-round" } },
                    new { tool = "clear_dp_value", args = new { elementId = "Button_61", propertyName = "Tag" } }
                }
            }),
            CancellationToken.None));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("snapshotId").GetString().Should().Be("snapshot_batch_inferred");
        executedTools.Should().Equal("set_dp_value", "clear_dp_value");
        capturedSnapshotArgs.Should().NotBeNull();
        capturedSnapshotArgs!.Value.GetProperty("processId").GetInt32().Should().Be(12345);
        capturedSnapshotArgs.Value.GetProperty("elementId").GetString().Should().Be("Button_61");
        capturedSnapshotArgs.Value.GetProperty("propertyNames").EnumerateArray()
            .Select(item => item.GetString()).Should().Equal("Tag");
        capturedSnapshotArgs.Value.GetProperty("includeFocus").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_WithCaptureSnapshotTrueAndMultipleDpTargets_ShouldRequireExplicitSnapshotObject()
    {
        var snapshotCalled = false;
        var tool = new BatchMutateTool(
            new SessionManager(),
            (_, _, _) => Task.FromResult<object>(new { success = true }),
            (_, _) =>
            {
                snapshotCalled = true;
                return Task.FromResult<object>(new { success = true, snapshotId = "unsafe" });
            },
            null);

        var result = JsonSerializer.SerializeToElement(await tool.ExecuteAsync(
            ToJsonElement(new
            {
                processId = 12345,
                captureSnapshot = true,
                mutations = new object[]
                {
                    new { tool = "set_dp_value", args = new { elementId = "Button_61", propertyName = "Tag", value = "one" } },
                    new { tool = "set_dp_value", args = new { elementId = "TextBox_70", propertyName = "Text", value = "two" } }
                }
            }),
            CancellationToken.None));

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
        result.GetProperty("error").GetString().Should().Contain("captureSnapshot=true");
        result.GetProperty("error").GetString().Should().Contain("explicit captureSnapshot object");
        snapshotCalled.Should().BeFalse();
    }
}
