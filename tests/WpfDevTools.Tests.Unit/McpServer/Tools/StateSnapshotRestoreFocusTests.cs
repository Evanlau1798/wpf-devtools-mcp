using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.State;
using WpfDevTools.Mcp.Server.Tools;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public sealed partial class StateSnapshotRestoreVerificationTests
{
    [Theory]
    [InlineData(null, "clear_focus")]
    [InlineData("NameTextBox", "focus_element")]
    public async Task RestoreStateSnapshot_ShouldVerifyCapturedFocusBaseline(
        string? focusedElementId,
        string expectedMutation)
    {
        var processId = NextSyntheticProcessId();
        using var connected = await CreateConnectedSessionAsync(
            processId,
            request => request.Method switch
            {
                "clear_focus" or "focus_element" => new { success = true },
                "get_focus_state" => new
                {
                    success = true,
                    focusKind = focusedElementId == null ? "None" : "Logical",
                    focusedElementId
                },
                _ => new { success = false, error = $"Unexpected method '{request.Method}'." }
            });

        const string snapshotId = "snapshot_focus_verified";
        connected.SessionManager.SaveStateSnapshot(processId, CreateSnapshot(
            snapshotId,
            focus: new StoredFocusSnapshot(
                focusedElementId == null ? "None" : "Logical",
                focusedElementId)));

        var result = JsonSerializer.SerializeToElement(await new RestoreStateSnapshotTool(connected.SessionManager)
            .ExecuteAsync(ToJsonElement(new { processId, snapshotId }), CancellationToken.None));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("restoredFocus").GetBoolean().Should().BeTrue();
        result.GetProperty("warnings").GetArrayLength().Should().Be(0);
        connected.RequestMethods.Should().Equal(expectedMutation, "get_focus_state");
        connected.SessionManager.TryGetStateSnapshot(processId, snapshotId, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(null, "clear_focus")]
    [InlineData("NameTextBox", "focus_element")]
    public async Task RestoreStateSnapshot_WhenFocusReadBackDiffers_ShouldFailClosedAndRetainSnapshot(
        string? focusedElementId,
        string expectedMutation)
    {
        var processId = NextSyntheticProcessId();
        using var connected = await CreateConnectedSessionAsync(
            processId,
            request => request.Method switch
            {
                "clear_focus" or "focus_element" => new { success = true },
                "get_focus_state" => new
                {
                    success = true,
                    focusKind = "Logical",
                    focusedElementId = "UnexpectedTextBox"
                },
                _ => new { success = false, error = $"Unexpected method '{request.Method}'." }
            });

        const string snapshotId = "snapshot_focus_mismatch";
        connected.SessionManager.SaveStateSnapshot(processId, CreateSnapshot(
            snapshotId,
            focus: new StoredFocusSnapshot(
                focusedElementId == null ? "None" : "Logical",
                focusedElementId)));

        var result = JsonSerializer.SerializeToElement(await new RestoreStateSnapshotTool(connected.SessionManager)
            .ExecuteAsync(ToJsonElement(new { processId, snapshotId }), CancellationToken.None));

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("restoredFocus").GetBoolean().Should().BeFalse();
        result.GetProperty("warnings")[0].GetString().Should().Contain("Focus restore verification failed");
        connected.RequestMethods.Should().Equal(expectedMutation, "get_focus_state");
        connected.SessionManager.TryGetStateSnapshot(processId, snapshotId, out _).Should().BeTrue();
    }
}
