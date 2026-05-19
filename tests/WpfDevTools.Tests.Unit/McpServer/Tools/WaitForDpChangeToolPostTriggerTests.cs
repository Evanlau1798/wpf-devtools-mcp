using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using Xunit;
using static WpfDevTools.Tests.Unit.TestHelpers;
using static WpfDevTools.Tests.Unit.McpServer.Tools.WaitForDpChangeToolTestHarness;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

[Collection("TimingSensitive")]
public sealed class WaitForDpChangeToolPostTriggerTests
{
    [Fact]
    public async Task Execute_WhenTriggerCompletesButBudgetExpiresBeforePolling_ShouldReturnTimedOutWithoutReconnectFlags()
    {
        const int processId = 4778;
        using var connected = await CreateDelayedAfterTriggerSnapshotSessionAsync(
            processId,
            mutationDelayMs: 30,
            afterTriggerSnapshotDelayMs: 350);
        var waitTool = new WaitForDpChangeTool(connected.SessionManager);

        var waitResult = await waitTool.ExecuteAsync(
            ToJsonElement(new
            {
                processId,
                propertyName = "Text",
                expectedValue = JsonSerializer.SerializeToElement("after"),
                timeoutMs = 300,
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

        var waitJson = JsonSerializer.SerializeToElement(waitResult);

        waitJson.GetProperty("success").GetBoolean().Should().BeTrue();
        waitJson.GetProperty("timedOut").GetBoolean().Should().BeTrue();
        waitJson.GetProperty("completionReason").GetString().Should().Be("TimedOut");
        waitJson.GetProperty("stateAfterTimeoutUnknown").GetBoolean().Should().BeFalse();
        waitJson.GetProperty("requiresReconnect").GetBoolean().Should().BeFalse();
        waitJson.GetProperty("currentValue").GetString().Should().Be("after");
        connected.SessionManager.GetPipeClient(processId)!.IsConnected.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_ShouldRequestBindingSettlementBeforePollingSnapshot()
    {
        const int processId = 4646;
        using var connected = await CreateBindingSettlementSessionAsync(processId);

        var waitTool = new WaitForDpChangeTool(connected.SessionManager);
        var mutateTool = new NoPiggybackModifyViewModelTool(connected.SessionManager);

        var waitTask = waitTool.ExecuteAsync(
            ToJsonElement(new
            {
                processId,
                propertyName = "Text",
                expectedValue = JsonSerializer.SerializeToElement("after"),
                timeoutMs = 1000,
                pollIntervalMs = 50
            }),
            CancellationToken.None);

        await Task.Delay(120);

        var mutateResult = await mutateTool.ExecuteAsync(
            ToJsonElement(new
            {
                processId,
                propertyName = "SearchText",
                value = JsonSerializer.SerializeToElement("after")
            }),
            CancellationToken.None);

        var waitResult = await waitTask;

        var mutateJson = JsonSerializer.SerializeToElement(mutateResult);
        var waitJson = JsonSerializer.SerializeToElement(waitResult);

        mutateJson.GetProperty("success").GetBoolean().Should().BeTrue();
        waitJson.GetProperty("success").GetBoolean().Should().BeTrue();
        waitJson.GetProperty("changed").GetBoolean().Should().BeTrue();
        waitJson.GetProperty("timedOut").GetBoolean().Should().BeFalse();
        waitJson.GetProperty("completionReason").GetString().Should().Be("ExpectedValueReached");
        connected.RequestPayloads.Should().Contain(payload =>
            payload.method == "get_dp_value_source" &&
            payload.settleBindings);
    }

    private sealed class NoPiggybackModifyViewModelTool(SessionManager sessionManager) : PipeConnectedToolBase(sessionManager)
    {
        public async Task<object> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken)
        {
            var (processId, parameters, error) = GenericPipeTool.ExtractElementPropertyAndValueParams(_sessionManager, arguments);
            if (error != null)
            {
                return error;
            }

            return await SendInspectorRequestWithoutPiggybackAsync(
                processId,
                "modify_viewmodel",
                parameters,
                cancellationToken);
        }
    }
}
