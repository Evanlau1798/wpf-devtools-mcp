using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.State;
using WpfDevTools.Mcp.Server.Tools;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public sealed partial class StateSnapshotRestoreVerificationTests
{
    [Fact]
    public async Task RestoreStateSnapshot_WhenDpVerificationRewritesViewModel_ShouldFailFinalConvergenceWithoutRetry()
    {
        var processId = NextSyntheticProcessId();
        var selectedIndex = "1";
        var queueStatus = "Mutated";
        var modifyCallCount = 0;
        using var connected = await CreateConnectedSessionAsync(
            processId,
            request => request.Method switch
            {
                "set_dp_value" => RestoreSelection(),
                "modify_viewmodel" => RestoreQueueStatus(request.Params!.Value.GetProperty("value").GetString()),
                "get_viewmodel" => new
                {
                    success = true,
                    typeName = "SampleViewModel",
                    properties = new object[]
                    {
                        new { name = "QueueStatus", type = "String", value = queueStatus, canWrite = true }
                    }
                },
                "get_dp_value_source" => VerifySelectionAndRewriteStatus(),
                _ => new { success = false, error = $"Unexpected method '{request.Method}'." }
            });

        const string snapshotId = "snapshot_delayed_vm_rewrite";
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
                    "Grid_1", "QueueStatus", "String", "Ready", CanRestore: true, SkipReason: null)
            ]));

        var result = JsonSerializer.SerializeToElement(await new RestoreStateSnapshotTool(connected.SessionManager)
            .ExecuteAsync(ToJsonElement(new { processId, snapshotId }), CancellationToken.None));

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("restoredViewModelProperties")[0].GetProperty("verified").GetBoolean().Should().BeFalse();
        result.GetProperty("restoredViewModelProperties")[0].GetProperty("currentValue").GetString().Should().Be("Delayed rewrite");
        result.GetProperty("warnings").EnumerateArray()
            .Select(item => item.GetString())
            .Should().Contain(item => item!.Contains("ViewModel final restore verification failed", StringComparison.Ordinal));
        modifyCallCount.Should().Be(1);
        connected.RequestMethods.Should().Equal(
            "set_dp_value", "modify_viewmodel", "get_viewmodel", "get_dp_value_source", "get_viewmodel");
        connected.SessionManager.TryGetStateSnapshot(processId, snapshotId, out _).Should().BeTrue();

        object RestoreSelection()
        {
            selectedIndex = "0";
            return new { success = true, propertyName = "SelectedIndex", newValue = selectedIndex };
        }

        object RestoreQueueStatus(string? value)
        {
            modifyCallCount++;
            queueStatus = value ?? string.Empty;
            return new { success = true, propertyName = "QueueStatus", newValue = queueStatus };
        }

        object VerifySelectionAndRewriteStatus()
        {
            queueStatus = "Delayed rewrite";
            return new
            {
                success = true,
                propertyName = "SelectedIndex",
                currentValue = selectedIndex,
                hadLocalValue = true,
                localValue = selectedIndex,
                baseValueSource = "LocalValue",
                isExpression = false
            };
        }
    }
}
