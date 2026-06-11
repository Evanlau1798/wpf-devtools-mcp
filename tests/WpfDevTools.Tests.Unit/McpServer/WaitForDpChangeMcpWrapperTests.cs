using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.McpTools;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Tests.Unit.McpServer.Tools;

namespace WpfDevTools.Tests.Unit.McpServer;

[Collection("ToolCallHelperState")]
public class WaitForDpChangeMcpWrapperTests
{
    [Fact]
    public async Task WaitForDpChange_WhenProtocolCancellationFiresDuringPolling_ShouldPropagateCancellation()
    {
        const int processId = 5800;
        var (session, initialSnapshotResponded) = await WaitForDpChangeToolTestHarness.CreateInitialSnapshotSignalSessionAsync(processId);
        using var connected = session;
        using var cancellation = new CancellationTokenSource();
        var beforePollDelay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePollDelay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        WaitForDpChangeTool.BeforePollDelayForTesting = async () =>
        {
            beforePollDelay.TrySetResult();
            await releasePollDelay.Task;
        };

        try
        {
            var waitTask = DependencyPropertyMcpTools.WaitForDpChange(
                connected.SessionManager,
                propertyName: "Text",
                processId: processId,
                timeoutMs: 1000,
                pollIntervalMs: 500,
                cancellationToken: cancellation.Token);

            await initialSnapshotResponded.WaitAsync(TimeSpan.FromSeconds(2));
            await beforePollDelay.Task.WaitAsync(TimeSpan.FromSeconds(2));
            cancellation.Cancel();
            releasePollDelay.TrySetResult();

            Func<Task> act = async () => await waitTask;

            await act.Should().ThrowAsync<OperationCanceledException>();
            connected.RequestMethods.Should().ContainSingle(method => method == "get_dp_value_source");
        }
        finally
        {
            releasePollDelay.TrySetResult();
            WaitForDpChangeTool.BeforePollDelayForTesting = null;
        }
    }

    [Fact]
    public async Task WaitForDpChangeAfterMutation_WhenProtocolCancellationFiresDuringTriggerMutation_ShouldPropagateCancellation()
    {
        const int processId = 5802;
        var (session, triggerRequestSeen, releaseTrigger) = await WaitForDpChangeToolTestHarness.CreateBlockedTriggerSignalSessionAsync(processId);
        using var connected = session;
        using var cancellation = new CancellationTokenSource();

        try
        {
            var waitTask = DependencyPropertyMcpTools.WaitForDpChangeAfterMutation(
                connected.SessionManager,
                propertyName: "Text",
                triggerMutation: JsonSerializer.SerializeToElement(new
                {
                    tool = "modify_viewmodel",
                    args = new
                    {
                        propertyName = "Name",
                        value = "after"
                    }
                }),
                processId: processId,
                timeoutMs: 1000,
                pollIntervalMs: 100,
                expectedValue: JsonSerializer.SerializeToElement("after"),
                cancellationToken: cancellation.Token);

            await triggerRequestSeen.WaitAsync(TimeSpan.FromSeconds(2));
            cancellation.Cancel();

            Func<Task> act = async () => await waitTask;

            await act.Should().ThrowAsync<OperationCanceledException>();
            connected.RequestMethods.Should().Contain("modify_viewmodel");
        }
        finally
        {
            releaseTrigger.TrySetResult();
        }
    }

    [Fact]
    public async Task WaitForDpChangeAfterMutation_WhenTriggerExhaustsBudget_ShouldPreservePublicTimeoutContract()
    {
        const int processId = 5801;
        using var connected = await WaitForDpChangeToolTestHarness.CreateDelayedTriggerSessionAsync(processId, mutationDelayMs: 250);

        var result = await DependencyPropertyMcpTools.WaitForDpChangeAfterMutation(
            connected.SessionManager,
            propertyName: "Text",
            triggerMutation: JsonSerializer.SerializeToElement(new
            {
                tool = "modify_viewmodel",
                args = new
                {
                    propertyName = "Name",
                    value = "after"
                }
            }),
            processId: processId,
            timeoutMs: 100,
            pollIntervalMs: 50,
            expectedValue: JsonSerializer.SerializeToElement("after"),
            cancellationToken: CancellationToken.None);

        result.IsError.Should().BeFalse();
        result.StructuredContent.Should().NotBeNull();

        var payload = result.StructuredContent!.Value;
        payload.GetProperty("success").GetBoolean().Should().BeTrue();
        payload.GetProperty("timedOut").GetBoolean().Should().BeTrue();
        payload.GetProperty("completionReason").GetString().Should().Be("TriggerMutationTimedOut");
        payload.GetProperty("stateAfterTimeoutUnknown").GetBoolean().Should().BeTrue();
        payload.GetProperty("requiresReconnect").GetBoolean().Should().BeTrue();
        connected.SessionManager.GetPipeClient(processId)!.IsConnected.Should().BeFalse();
    }
}