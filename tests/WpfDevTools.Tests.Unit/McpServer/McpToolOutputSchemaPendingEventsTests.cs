using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class McpToolOutputSchemaPendingEventsTests
{
    [Theory]
    [InlineData("pendingEventsPiggybackFailed")]
    [InlineData("pendingEventsPiggybackFailureType")]
    [InlineData("pendingEventsMayRemainBuffered")]
    [InlineData("pendingEventsPiggybackRequiresReconnect")]
    [InlineData("pendingEventsStateAfterTimeoutUnknown")]
    [InlineData("pendingEventsPiggybackSuggestedAction")]
    public void Apply_ShouldExposePendingEventsPiggybackRecoveryFields(string propertyName)
    {
        var tool = new Tool
        {
            Name = "get_binding_errors",
            InputSchema = JsonSerializer.SerializeToElement(new { type = "object" })
        };

        McpToolOutputSchemas.Apply(tool);

        tool.OutputSchema!.Value.GetProperty("properties")
            .TryGetProperty(propertyName, out _)
            .Should().BeTrue($"{propertyName} is emitted when piggyback drain recovery is needed");
    }

    [Theory]
    [InlineData("restoreRequired")]
    [InlineData("restoreStatus")]
    [InlineData("restoreSuggestedAction")]
    public void Apply_ShouldExposeMutationRestoreStatusFields(string propertyName)
    {
        var tool = new Tool
        {
            Name = "set_dp_value",
            InputSchema = JsonSerializer.SerializeToElement(new { type = "object" })
        };

        McpToolOutputSchemas.Apply(tool);

        tool.OutputSchema!.Value.GetProperty("properties")
            .TryGetProperty(propertyName, out _)
            .Should().BeTrue($"{propertyName} is emitted by mutation success responses");
    }
}
