using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.Tools;
using static WpfDevTools.Tests.Unit.TestHelpers;
using static WpfDevTools.Tests.Unit.McpServer.Tools.WaitForDpChangeToolTestHarness;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public sealed partial class WaitForDpChangeToolConcurrencyTests
{
    [Fact]
    public async Task Execute_WithInjectedTriggerMutationTimeout_ShouldNotForceReconnectWhenExecutorDoesNotUsePipe()
    {
        const int processId = 4769;
        using var connected = await CreateConnectedSessionAsync(processId);
        var executorStarted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseExecutor = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var executorCompleted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var waitTool = new WaitForDpChangeTool(
            connected.SessionManager,
            async (_, _) =>
            {
                executorStarted.TrySetResult(null);
                try
                {
                    await releaseExecutor.Task;
                    return new { success = true };
                }
                finally
                {
                    executorCompleted.TrySetResult(null);
                }
            },
            triggerMutationTimeoutRequiresReconnect: false);

        try
        {
            var waitTask = waitTool.ExecuteAsync(
                ToJsonElement(new
                {
                    processId,
                    propertyName = "Text",
                    expectedValue = JsonSerializer.SerializeToElement("after"),
                    timeoutMs = 500,
                    pollIntervalMs = 50,
                    triggerMutation = new
                    {
                        tool = "modify_viewmodel",
                        args = new
                        {
                            propertyName = "Name",
                            value = "after"
                        }
                    }
                }),
                CancellationToken.None);
            await executorStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var waitResult = await waitTask;

            var waitJson = JsonSerializer.SerializeToElement(waitResult);
            waitJson.GetProperty("success").GetBoolean().Should().BeTrue();
            waitJson.GetProperty("timedOut").GetBoolean().Should().BeTrue();
            waitJson.GetProperty("completionReason").GetString().Should().Be("TriggerMutationTimedOut");
            waitJson.GetProperty("stateAfterTimeoutUnknown").GetBoolean().Should().BeTrue();
            waitJson.GetProperty("requiresReconnect").GetBoolean().Should().BeFalse();
            connected.SessionManager.GetPipeClient(processId)!.IsConnected.Should().BeTrue(
                "an injected non-pipe timeout should not tear down a healthy connection");
        }
        finally
        {
            releaseExecutor.TrySetResult(null);
            await executorCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}
