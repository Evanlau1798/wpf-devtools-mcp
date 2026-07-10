using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.State;
using WpfDevTools.Mcp.Server.Tools;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public sealed partial class StateSnapshotRestoreVerificationTests
{
    [Fact]
    public async Task RestoreStateSnapshot_ShouldRetryClearWhenControlRecreatesEquivalentLocalValue()
    {
        var processId = NextSyntheticProcessId();
        var valueSourceReads = 0;
        using var connected = await CreateConnectedSessionAsync(
            processId,
            request => request.Method switch
            {
                "clear_dp_value" => new
                {
                    success = true,
                    propertyName = "Text",
                    newValue = ""
                },
                "get_dp_value_source" => new
                {
                    success = true,
                    propertyName = "Text",
                    currentValue = "",
                    baseValueSource = ++valueSourceReads == 1 ? "LocalValue" : "Default",
                    isExpression = false
                },
                _ => new { success = false, error = $"Unexpected method '{request.Method}'." }
            });

        const string snapshotId = "snapshot_default_source_local_echo";
        connected.SessionManager.SaveStateSnapshot(processId, CreateSnapshot(
            snapshotId,
            dependencyProperties:
            [
                new StoredDependencyPropertySnapshot(
                    "AutoSuggestBox_1",
                    "Text",
                    HadLocalValue: false,
                    LocalValue: null,
                    CurrentValue: "",
                    BaseValueSource: "Default")
            ]));

        var result = JsonSerializer.SerializeToElement(await new RestoreStateSnapshotTool(connected.SessionManager)
            .ExecuteAsync(ToJsonElement(new { processId, snapshotId }), CancellationToken.None));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("warnings").GetArrayLength().Should().Be(0);
        var verification = result.GetProperty("restoredDependencyProperties")[0];
        verification.GetProperty("verified").GetBoolean().Should().BeTrue();
        verification.GetProperty("expectedBaseValueSource").GetString().Should().Be("Default");
        verification.GetProperty("currentBaseValueSource").GetString().Should().Be("Default");
        connected.RequestMethods.Should().Equal(
            "clear_dp_value",
            "get_dp_value_source",
            "clear_dp_value",
            "get_dp_value_source");
    }
}
