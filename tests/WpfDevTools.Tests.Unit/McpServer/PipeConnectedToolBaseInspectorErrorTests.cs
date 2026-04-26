using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Enums;
using WpfDevTools.Shared.Messages;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class PipeConnectedToolBaseInspectorErrorTests
{
    [Fact]
    public void CreateInspectorError_WhenTimeoutDataRequiresReconnect_ShouldProjectRecoveryContract()
    {
        var inspectorError = new InspectorError
        {
            Code = ErrorCode.Timeout,
            Message = "Request cancelled or timed out",
            Data = JsonSerializer.SerializeToElement(new
            {
                stateAfterTimeoutUnknown = true,
                requiresReconnect = true,
                suggestedAction = "Reconnect to process 12345 and re-read state before retrying.",
                processId = 12345,
                timeoutSeconds = 30
            })
        };

        var payload = JsonSerializer.SerializeToElement(
            PipeConnectedToolBase.CreateInspectorError(inspectorError));

        payload.GetProperty("success").GetBoolean().Should().BeFalse();
        payload.GetProperty("errorCode").GetString().Should().Be("Timeout");
        payload.GetProperty("stateAfterTimeoutUnknown").GetBoolean().Should().BeTrue();
        payload.GetProperty("requiresReconnect").GetBoolean().Should().BeTrue();
        payload.GetProperty("suggestedAction").GetString().Should().Contain("Reconnect");
        payload.GetProperty("processId").GetInt32().Should().Be(12345);
        payload.GetProperty("timeoutSeconds").GetInt32().Should().Be(30);

        var recovery = payload.GetProperty("recovery");
        recovery.GetProperty("stateAfterTimeoutUnknown").GetBoolean().Should().BeTrue();
        recovery.GetProperty("requiresReconnect").GetBoolean().Should().BeTrue();
        recovery.GetProperty("suggestedAction").GetString().Should().Contain("Reconnect");
    }
}
