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
