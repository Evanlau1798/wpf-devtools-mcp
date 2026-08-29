using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.State;
using WpfDevTools.Mcp.Server.Tools;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public sealed partial class StateSnapshotRestoreVerificationTests
{
    [Fact]
    public async Task RestoreStateSnapshot_ShouldApplySelectionBeforeRestoringDependentViewModelState()
    {
        var processId = NextSyntheticProcessId();
        var selectedIndex = "1";
        var selectedEdition = "Beta";
        var queueStatus = "Queued Beta";
        using var connected = await CreateConnectedSessionAsync(
            processId,
            request => request.Method switch
            {
                "set_dp_value" => RestoreSelection(),
                "modify_viewmodel" => RestoreStatus(request.Params!.Value.GetProperty("value").GetString()),
                "get_dp_value_source" => new
                {
                    success = true,
                    propertyName = "SelectedIndex",
                    currentValue = selectedIndex,
                    hadLocalValue = true,
                    localValue = selectedIndex,
                    baseValueSource = "LocalValue",
                    isExpression = false
                },
                "get_viewmodel" => new
                {
                    success = true,
                    typeName = "SampleViewModel",
                    properties = new object[]
                    {
                        new { name = "SelectedEdition", type = "Edition", value = selectedEdition, canWrite = true },
                        new { name = "QueueStatus", type = "String", value = queueStatus, canWrite = true }
                    }
                },
                _ => new { success = false, error = $"Unexpected method '{request.Method}'." }
            });

        const string snapshotId = "snapshot_selection_side_effect";
        connected.SessionManager.SaveStateSnapshot(processId, CreateSnapshot(
            snapshotId,
            dependencyProperties:
            [
                new StoredDependencyPropertySnapshot(
                    "Grid_1", "SelectedIndex", HadLocalValue: true, LocalValue: "0",
                    CurrentValue: "0", BaseValueSource: "LocalValue")
            ],
            viewModelProperties:
            [
                new StoredViewModelPropertySnapshot(
                    "Grid_1", "SelectedEdition", "Edition", "Alpha", CanRestore: false,
                    SkipReason: "Complex reference."),
                new StoredViewModelPropertySnapshot(
                    "Grid_1", "QueueStatus", "String", "Ready", CanRestore: true,
                    SkipReason: null)
            ]));

        var result = JsonSerializer.SerializeToElement(await new RestoreStateSnapshotTool(connected.SessionManager)
            .ExecuteAsync(ToJsonElement(new { processId, snapshotId }), CancellationToken.None));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("warnings").GetArrayLength().Should().Be(0);
        result.GetProperty("skippedViewModelProperties")[0].GetProperty("verified").GetBoolean().Should().BeTrue();
        queueStatus.Should().Be("Ready");
        connected.RequestMethods.Should().Equal(
            "set_dp_value", "get_viewmodel", "modify_viewmodel", "get_viewmodel", "get_dp_value_source");

        object RestoreSelection()
        {
            selectedIndex = "0";
            selectedEdition = "Alpha";
            queueStatus = "Selected Alpha";
            return new { success = true, propertyName = "SelectedIndex", newValue = selectedIndex };
        }

        object RestoreStatus(string? value)
        {
            queueStatus = value ?? string.Empty;
            return new { success = true, propertyName = "QueueStatus", newValue = queueStatus };
        }
    }
}
