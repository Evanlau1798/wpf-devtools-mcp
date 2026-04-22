using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Tests.Unit.McpServer;

[Collection("ToolCallHelperState")]
public class ToolCallHelperTimeoutContractTests
{
    [Fact]
    public void ResolveExecutionTimeoutSeconds_WhenWaitForDpChangeRequestsLongerBudget_ShouldAddHeadroom()
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", 1234),
            ("propertyName", "Text"),
            ("timeoutMs", 6000));

        var timeoutSeconds = ToolCallHelper.ResolveExecutionTimeoutSeconds(
            "WaitForDpChange",
            args,
            timeoutSeconds: null);

        timeoutSeconds.Should().Be(8);
    }

    [Fact]
    public void ResolveExecutionTimeoutSeconds_WhenWaitForDpChangeOmitsTimeoutMs_ShouldStillAddDefaultHeadroom()
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", 1234),
            ("propertyName", "Text"));

        var timeoutSeconds = ToolCallHelper.ResolveExecutionTimeoutSeconds(
            "WaitForDpChange",
            args,
            timeoutSeconds: null);

        timeoutSeconds.Should().Be(7);
    }

    [Fact]
    public void ResolveExecutionTimeoutSeconds_WhenWaitForDpChangeAfterMutationRequestsLongerBudget_ShouldAddHeadroom()
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", 1234),
            ("propertyName", "Text"),
            ("timeoutMs", 6000),
            ("triggerMutation", new { tool = "modify_viewmodel", args = new { propertyName = "Name", value = "Ready" } }));

        var timeoutSeconds = ToolCallHelper.ResolveExecutionTimeoutSeconds(
            "WaitForDpChangeAfterMutation",
            args,
            timeoutSeconds: null);

        timeoutSeconds.Should().Be(8);
    }

    [Fact]
    public void ResolveExecutionTimeoutSeconds_WhenWaitForDpChangeAfterMutationOmitsTimeoutMs_ShouldStillAddDefaultHeadroom()
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", 1234),
            ("propertyName", "Text"),
            ("triggerMutation", new { tool = "modify_viewmodel", args = new { propertyName = "Name", value = "Ready" } }));

        var timeoutSeconds = ToolCallHelper.ResolveExecutionTimeoutSeconds(
            "WaitForDpChangeAfterMutation",
            args,
            timeoutSeconds: null);

        timeoutSeconds.Should().Be(7);
    }

    [Fact]
    public void ResolveExecutionTimeoutSeconds_WhenExplicitOverrideProvided_ShouldPreferOverride()
    {
        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", 1234),
            ("propertyName", "Text"),
            ("timeoutMs", 6000));

        var timeoutSeconds = ToolCallHelper.ResolveExecutionTimeoutSeconds(
            "WaitForDpChange",
            args,
            timeoutSeconds: 3);

        timeoutSeconds.Should().Be(3);
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WhenToolTimesOut_ShouldReturnStableTimeoutContract()
    {
        Func<JsonElement?, CancellationToken, Task<object>> slowTool = async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return new { success = true };
        };

        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            slowTool,
            null,
            CancellationToken.None,
            timeoutSeconds: 1,
            toolName: "test_timeout_tool");

        result.IsError.Should().BeTrue();
        var structured = result.StructuredContent!.Value;
        structured.GetProperty("success").GetBoolean().Should().BeFalse();
        structured.GetProperty("errorCode").GetString().Should().Be("Timeout");
        structured.GetProperty("timeoutSeconds").GetInt32().Should().Be(1);
        structured.GetProperty("toolName").GetString().Should().Be("test_timeout_tool");
        structured.GetProperty("suggestedAction").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ExecuteAndWrapAsync_WhenPipeBackedToolTimesOut_ShouldRequireReconnect()
    {
        Func<JsonElement?, CancellationToken, Task<object>> slowTool = async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return new { success = true };
        };

        var args = ToolCallHelper.BuildJsonArgs(
            ("processId", 1234),
            ("elementId", "RootWindow"));

        var result = await ToolCallHelper.ExecuteAndWrapAsync(
            slowTool,
            args,
            CancellationToken.None,
            timeoutSeconds: 1,
            toolName: "GetVisualTree");

        result.IsError.Should().BeTrue();
        var structured = result.StructuredContent!.Value;
        structured.GetProperty("requiresReconnect").GetBoolean().Should().BeTrue();
        structured.GetProperty("processId").GetInt32().Should().Be(1234);
        structured.GetProperty("suggestedAction").GetString().Should().ContainEquivalentOf("reconnect");
    }
}
