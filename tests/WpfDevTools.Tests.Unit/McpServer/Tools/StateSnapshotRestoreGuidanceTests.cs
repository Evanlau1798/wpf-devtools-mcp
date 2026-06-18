using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.McpTools;
using WpfDevTools.Mcp.Server.Tools;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public sealed class StateSnapshotRestoreGuidanceTests
{
    [Fact]
    public async Task RestoreStateSnapshot_WithUnknownSnapshotId_ShouldSuggestFreshSnapshotAndDiagnostics()
    {
        var tool = new RestoreStateSnapshotTool(new SessionManager());

        var result = await tool.ExecuteAsync(ToJsonElement(new
        {
            processId = 51003,
            snapshotId = "expired-or-wrong-session"
        }), CancellationToken.None);

        var json = JsonSerializer.SerializeToElement(result);
        var hint = json.GetProperty("hint").GetString();

        json.GetProperty("success").GetBoolean().Should().BeFalse();
        json.GetProperty("errorCode").GetString().Should().Be("InvalidArgument");
        hint.Should().Contain("fresh snapshot");
        hint.Should().Contain("capture_state_snapshot");
        hint.Should().Contain("get_dp_value_source");
        hint.Should().Contain("get_bindings");
    }

    [Fact]
    public void RestoreStateSnapshotDescription_ShouldMentionFreshSnapshotAndDiagnostics()
    {
        var description = StateMcpToolDescriptions.RestoreStateSnapshot;

        description.Should().Contain("fresh snapshot");
        description.Should().Contain("capture_state_snapshot");
        description.Should().Contain("get_dp_value_source");
        description.Should().Contain("get_bindings");
    }
}
