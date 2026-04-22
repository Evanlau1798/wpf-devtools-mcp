using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.McpTools;
using WpfDevTools.Tests.Unit.McpServer.Tools;

namespace WpfDevTools.Tests.Unit.McpServer;

[Collection("ToolCallHelperState")]
public class WaitForDpChangeMcpWrapperTests
{
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