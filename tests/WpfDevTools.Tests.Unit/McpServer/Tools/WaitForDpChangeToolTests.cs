using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using Xunit;
using static WpfDevTools.Tests.Unit.TestHelpers;
using static WpfDevTools.Tests.Unit.McpServer.Tools.WaitForDpChangeToolTestHarness;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

[Collection("TimingSensitive")]
public sealed class WaitForDpChangeToolTests
{
    [Fact]
    public async Task Execute_WithoutConnection_ShouldReturnError()
    {
        var tool = new WaitForDpChangeTool(new SessionManager());
        var parameters = new { processId = 12345, propertyName = "Width", timeoutMs = 100 };

        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().Contain("not connected");
    }

    [Fact]
    public async Task Execute_WithMissingPropertyName_ShouldReturnError()
    {
        var sessionManager = new SessionManager();
        sessionManager.AddSession(12345);
        var tool = new WaitForDpChangeTool(sessionManager);
        var parameters = new { processId = 12345, timeoutMs = 100 };

        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().Contain("propertyName");
    }

    [Fact]
    public async Task Execute_WithValidParameters_ShouldReturnResult()
    {
        var sessionManager = new SessionManager();
        sessionManager.AddSession(12345);
        var tool = new WaitForDpChangeTool(sessionManager);
        var parameters = new { processId = 12345, propertyName = "Width", elementId = "myButton", timeoutMs = 100, pollIntervalMs = 50 };

        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task Execute_WithTimeoutAboveSafeHostBudget_ShouldReturnInvalidArgumentBeforeSnapshot()
    {
        const int processId = 4949;
        using var connected = await CreateConnectedSessionAsync(processId);
        var waitTool = new WaitForDpChangeTool(connected.SessionManager);

        var result = await waitTool.ExecuteAsync(
            ToJsonElement(new
            {
                processId,
                propertyName = "Text",
                timeoutMs = 25001,
                pollIntervalMs = 50,
                expectedValue = JsonSerializer.SerializeToElement("before")
            }),
            CancellationToken.None);

        var resultJson = JsonSerializer.SerializeToElement(result);
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
        resultJson.GetProperty("error").GetString().Should().Contain("25000");
        connected.RequestMethods.Should().BeEmpty("invalid wait budgets must be rejected before touching the inspector pipe");
    }

    [Fact]
    public async Task Execute_WhenBudgetExhaustedAfterInitialSnapshot_ShouldNotIssueFinalSnapshot()
    {
        const int processId = 4950;
        var state = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["currentValue"] = "before"
        };
        using var connected = await ConnectedWaitSessionBuilder.CreateAsync(
            processId,
            state,
            static async (request, currentState) =>
            {
                if (request.Method == "get_dp_value_source")
                {
                    await Task.Delay(30);
                    return new
                    {
                        success = true,
                        propertyName = "Text",
                        baseValueSource = "Local",
                        currentValue = currentState["currentValue"],
                        effectiveValue = currentState["currentValue"]
                    };
                }

                return new { success = true };
            });
        var waitTool = new WaitForDpChangeTool(connected.SessionManager);

        var result = await waitTool.ExecuteAsync(
            ToJsonElement(new
            {
                processId,
                propertyName = "Text",
                timeoutMs = 1,
                pollIntervalMs = 50,
                expectedValue = JsonSerializer.SerializeToElement("after")
            }),
            CancellationToken.None);

        var resultJson = JsonSerializer.SerializeToElement(result);
        resultJson.GetProperty("success").GetBoolean().Should().BeTrue();
        resultJson.GetProperty("timedOut").GetBoolean().Should().BeTrue();
        resultJson.GetProperty("completionReason").GetString().Should().Be("TimedOut");
        connected.RequestMethods.Should().ContainSingle(method => method == "get_dp_value_source");
    }

    [Fact]
    public async Task Execute_WhenInternalPollingExceedsSessionRateLimit_ShouldStillReturnTimeout()
    {
        const int processId = 4951;
        var state = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["currentValue"] = "before"
        };
        using var connected = await ConnectedWaitSessionBuilder.CreateAsync(
            processId,
            state,
            static (request, currentState) =>
            {
                var value = currentState["currentValue"];
                return Task.FromResult<object>(new
                {
                    success = true,
                    propertyName = "Text",
                    baseValueSource = "Local",
                    currentValue = value,
                    effectiveValue = value
                });
            },
            maxRequestsPerMinute: 2);
        var waitTool = new WaitForDpChangeTool(connected.SessionManager);

        var result = await waitTool.ExecuteAsync(
            ToJsonElement(new
            {
                processId,
                propertyName = "Text",
                timeoutMs = 500,
                pollIntervalMs = 50,
                expectedValue = JsonSerializer.SerializeToElement("after")
            }),
            CancellationToken.None);

        var resultJson = JsonSerializer.SerializeToElement(result);
        resultJson.GetProperty("success").GetBoolean().Should().BeTrue();
        resultJson.GetProperty("timedOut").GetBoolean().Should().BeTrue();
        resultJson.GetProperty("completionReason").GetString().Should().Be("TimedOut");
        resultJson.GetProperty("pollCount").GetInt32().Should().BeGreaterThan(1);
        connected.RequestMethods.Count(method => method == "get_dp_value_source").Should().BeGreaterThan(2);
    }

    [Fact]
    public async Task Execute_WhenFinalSnapshotReadTimesOut_ShouldRequireReconnectAndMarkStateUnknown()
    {
        const int processId = 4952;
        var snapshotReadCount = 0;
        var beforePollDelayCalls = 0;
        var previousBeforePollDelay = WaitForDpChangeTool.BeforePollDelayForTesting;
        using var connected = await ConnectedWaitSessionBuilder.CreateAsync(
            processId,
            new object(),
            async (request, _) =>
            {
                if (request.Method != "get_dp_value_source")
                {
                    return new { success = true };
                }

                snapshotReadCount++;
                if (snapshotReadCount >= 3)
                {
                    await Task.Delay(1500);
                }

                return new
                {
                    success = true,
                    propertyName = "Text",
                    baseValueSource = "Local",
                    currentValue = "before",
                    effectiveValue = "before"
                };
            });
        var waitTool = new WaitForDpChangeTool(connected.SessionManager);

        WaitForDpChangeTool.BeforePollDelayForTesting = async () =>
        {
            beforePollDelayCalls++;
            if (beforePollDelayCalls == 1)
            {
                await Task.Delay(300);
            }
        };

        object result;
        try
        {
            result = await waitTool.ExecuteAsync(
                ToJsonElement(new
                {
                    processId,
                    propertyName = "Text",
                    timeoutMs = 250,
                    pollIntervalMs = 50,
                    expectedValue = JsonSerializer.SerializeToElement("after")
                }),
                CancellationToken.None);
        }
        finally
        {
            WaitForDpChangeTool.BeforePollDelayForTesting = previousBeforePollDelay;
        }

        var resultJson = JsonSerializer.SerializeToElement(result);
        resultJson.GetProperty("success").GetBoolean().Should().BeTrue("payload was {0}", resultJson.ToString());
        resultJson.GetProperty("timedOut").GetBoolean().Should().BeTrue();
        resultJson.GetProperty("completionReason").GetString().Should().Be("TimedOut");
        resultJson.GetProperty("stateAfterTimeoutUnknown").GetBoolean().Should().BeTrue();
        resultJson.GetProperty("requiresReconnect").GetBoolean().Should().BeTrue();
        beforePollDelayCalls.Should().Be(1);
        connected.RequestMethods.Count(method => method == "get_dp_value_source").Should().Be(3);
    }

    [Fact]
    public async Task Execute_WhenCancelledDuringPollDelay_ShouldNotIssueAdditionalSnapshots()
    {
        const int processId = 4848;
        using var connected = await CreateConnectedSessionAsync(processId);
        var waitTool = new WaitForDpChangeTool(connected.SessionManager);
        using var cancellation = new CancellationTokenSource(75);

        var act = () => waitTool.ExecuteAsync(
            ToJsonElement(new
            {
                processId,
                propertyName = "Text",
                timeoutMs = 1000,
                pollIntervalMs = 500
            }),
            cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        connected.RequestMethods.Should().ContainSingle(method => method == "get_dp_value_source");
    }
}
