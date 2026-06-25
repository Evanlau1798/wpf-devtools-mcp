using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.Tools;
using static WpfDevTools.Tests.Unit.TestHelpers;
using static WpfDevTools.Tests.Unit.McpServer.Tools.WaitForDpChangeToolTestHarness;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

[Collection("TimingSensitive")]
public sealed class WaitForDpChangeToolCompatibilityTests
{
    [Fact]
    public async Task Execute_WithStringifiedTriggerMutationObject_ShouldAcceptCompatibilityPayload()
    {
        const int processId = 4747;
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
                triggerMutation = JsonSerializer.Serialize(new
                {
                    tool = "modify_viewmodel",
                    args = new
                    {
                        propertyName = "Name",
                        value = "after"
                    }
                })
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
    public async Task Execute_WithTriggerMutationArgumentsProperty_ShouldReturnActionableShapeError()
    {
        const int processId = 4748;
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
                    arguments = new
                    {
                        propertyName = "Name",
                        value = "after"
                    }
                }
            }),
            CancellationToken.None);

        var waitJson = JsonSerializer.SerializeToElement(waitResult);
        waitJson.GetProperty("success").GetBoolean().Should().BeFalse();
        waitJson.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
        waitJson.GetProperty("error").GetString().Should().Contain("triggerMutation");
        waitJson.GetProperty("error").GetString().Should().Contain("arguments");
        waitJson.GetProperty("error").GetString().Should().Contain("args");
        connected.RequestMethods.Should().NotContain("modify_viewmodel");
    }
}
