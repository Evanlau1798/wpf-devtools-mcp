using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using Xunit;
using static WpfDevTools.Tests.Unit.TestHelpers;
using static WpfDevTools.Tests.Unit.McpServer.Tools.WaitForDpChangeToolTestHarness;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public sealed class WaitForDpChangeToolConcurrencyTests
{
    [Fact]
    public async Task Execute_ShouldPollViaShortRequestsSoConcurrentMutationCanComplete()
    {
        const int processId = 4242;
        using var connected = await CreateConnectedSessionAsync(processId);

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

        using var mutateCts = new CancellationTokenSource(250);
        var mutateResult = await mutateTool.ExecuteAsync(
            ToJsonElement(new
            {
                processId,
                propertyName = "Name",
                value = JsonSerializer.SerializeToElement("after")
            }),
            mutateCts.Token);

        var waitResult = await waitTask;

        var mutateJson = JsonSerializer.SerializeToElement(mutateResult);
        var waitJson = JsonSerializer.SerializeToElement(waitResult);

        mutateJson.GetProperty("success").GetBoolean().Should().BeTrue();
        waitJson.GetProperty("success").GetBoolean().Should().BeTrue();
        waitJson.GetProperty("changed").GetBoolean().Should().BeTrue();
        waitJson.GetProperty("timedOut").GetBoolean().Should().BeFalse();
        waitJson.GetProperty("completionReason").GetString().Should().Be("ExpectedValueReached");

        connected.RequestMethods.Should().Contain("get_dp_value_source");
        connected.RequestMethods.Should().Contain("modify_viewmodel");
        connected.RequestMethods.Should().NotContain("wait_for_dp_change");
    }

    [Fact]
    public async Task Execute_WhenExpectedValueAppearsOnFinalRead_ShouldReturnReachedInsteadOfTimedOut()
    {
        const int processId = 4343;
        using var connected = await CreateBoundaryConnectedSessionAsync(processId);
        var waitTool = new WaitForDpChangeTool(connected.SessionManager);

        var waitResult = await waitTool.ExecuteAsync(
            ToJsonElement(new
            {
                processId,
                propertyName = "Text",
                expectedValue = JsonSerializer.SerializeToElement("after"),
                timeoutMs = 75,
                pollIntervalMs = 50
            }),
            CancellationToken.None);

        var waitJson = JsonSerializer.SerializeToElement(waitResult);

        waitJson.GetProperty("success").GetBoolean().Should().BeTrue();
        waitJson.GetProperty("changed").GetBoolean().Should().BeTrue();
        waitJson.GetProperty("timedOut").GetBoolean().Should().BeFalse();
        waitJson.GetProperty("completionReason").GetString().Should().Be("ExpectedValueReached");
        waitJson.GetProperty("currentValue").GetString().Should().Be("after");
    }

    [Fact]
    public async Task Execute_WithTriggerMutation_ShouldRunMutationBeforeWaiting()
    {
        const int processId = 4545;
        using var connected = await CreateConnectedSessionAsync(processId);
        var waitTool = new WaitForDpChangeTool(connected.SessionManager);

        var waitResult = await waitTool.ExecuteAsync(
            ToJsonElement(new
            {
                processId,
                propertyName = "Text",
                expectedValue = JsonSerializer.SerializeToElement("after"),
                timeoutMs = 1000,
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
        waitJson.GetProperty("changed").GetBoolean().Should().BeTrue();
        waitJson.GetProperty("timedOut").GetBoolean().Should().BeFalse();
        waitJson.GetProperty("completionReason").GetString().Should().Be("ExpectedValueReached");
        connected.RequestMethods.Should().Contain("modify_viewmodel");
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
