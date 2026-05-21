using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Mcp.Server.McpResources;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed partial class ResponseContractResourceTests
{
    [Fact]
    public void ResponseContractResource_ShouldListMutationSafeHighValueContracts()
    {
        using var document = JsonDocument.Parse(CapabilityResources.GetResponseContract());
        var highValueTools = document.RootElement.GetProperty("highValueTools");

        AssertHighValueToolContract(
            highValueTools,
            "capture_state_snapshot",
            "state-snapshot-capture",
            topLevelFields: ["snapshotId", "snapshotName", "snapshotSummary", "snapshotCompleteness", "warnings"],
            requestParameters: ["elementId", "propertyNames", "viewModelPropertyNames", "includeFocus", "snapshotName"],
            nestedResponsePaths: [
                "snapshotSummary.dependencyPropertyCount",
                "snapshotSummary.viewModelPropertyCount",
                "snapshotSummary.capturedFocus",
                "snapshotCompleteness.bindingErrorBaselineCaptured",
                "snapshotCompleteness.validationBaselineCaptured",
                "warnings[]"
            ]);

        AssertHighValueToolContract(
            highValueTools,
            "get_state_diff",
            "state-diff",
            topLevelFields: ["snapshotId", "trigger", "propertyChanges", "viewModelChanges", "newBindingErrors", "resolvedBindingErrors", "validationChanges", "focusChange"],
            requestParameters: ["snapshotId", "trigger"],
            nestedResponsePaths: ["propertyChanges[].propertyName", "viewModelChanges[].propertyName", "focusChange.changed"]);

        AssertHighValueToolContract(
            highValueTools,
            "restore_state_snapshot",
            "state-snapshot-restore",
            topLevelFields: ["restoredDependencyPropertyCount", "restoredDependencyProperties", "skippedDependencyProperties", "restoredViewModelProperties", "skippedViewModelProperties", "restoredFocus", "warnings"],
            requestParameters: ["snapshotId", "removeAfterRestore"],
            nestedResponsePaths: ["restoredDependencyProperties[].propertyName", "skippedDependencyProperties[].reason", "restoredViewModelProperties[].verified"]);

        AssertHighValueToolContract(
            highValueTools,
            "batch_mutate",
            "batch-mutation",
            topLevelFields: ["snapshotId", "mutationCount", "executedMutationCount", "failedMutationCount", "skippedMutationCount", "mutations", "stateDiff", "rollback", "recovery"],
            requestParameters: ["captureSnapshot", "includeDiff", "mutations", "trigger"],
            nestedResponsePaths: ["mutations[].tool", "mutations[].success", "stateDiff.snapshotId", "rollback.params.snapshotId", "recovery.tool"]);
    }
}
