using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.McpTools;
using WpfDevTools.Mcp.Server.State;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer;

[Collection("ToolCallHelperState")]
public sealed class McpToolsMutationNavigationWrapperTests : IDisposable
{
    private const int ProcessId = 65001;
    private const string SnapshotId = "snapshot_wrapper_navigation";

    private readonly SessionManager _sessionManager = new();
    private readonly IDisposable _toolCallHelperScope = ToolCallHelper.BeginTestScope();

    public void Dispose()
    {
        _sessionManager.Dispose();
        _toolCallHelperScope.Dispose();
    }

    public static TheoryData<string, Func<SessionManager, Task<CallToolResult>>> SnapshotAwareMutationWrappers()
    {
        var value = JsonSerializer.SerializeToElement("new value");
        var triggerMutation = JsonSerializer.SerializeToElement(new
        {
            tool = "set_dp_value",
            args = new
            {
                propertyName = "Text",
                value = "changed"
            }
        });

        return new TheoryData<string, Func<SessionManager, Task<CallToolResult>>>
        {
            { "clear_dp_value", manager => DependencyPropertyMcpTools.ClearDpValue(manager, "Text", ProcessId, "NameTextBox") },
            { "wait_for_dp_change_after_mutation", manager => DependencyPropertyMcpTools.WaitForDpChangeAfterMutation(manager, "Text", triggerMutation, ProcessId, "NameTextBox", timeoutMs: 1, pollIntervalMs: 1) },
            { "force_binding_update", manager => BindingMcpTools.ForceBindingUpdate(manager, "Text", ProcessId, "NameTextBox") },
            { "focus_element", manager => InteractionMcpTools.FocusElement(manager, "NameTextBox", ProcessId) },
            { "drag_and_drop", manager => InteractionMcpTools.DragAndDrop(manager, "SourceItem", "TargetItem", ProcessId) },
            { "scroll_to_element", manager => InteractionMcpTools.ScrollToElement(manager, "NameTextBox", ProcessId) },
            { "simulate_keyboard", manager => InteractionMcpTools.SimulateKeyboard(manager, "Enter", ProcessId, "NameTextBox") },
            { "override_style_setter", manager => StyleMcpTools.OverrideStyleSetter(manager, "Background", value, "NameTextBox", ProcessId) },
            { "invalidate_layout", manager => LayoutMcpTools.InvalidateLayout(manager, ProcessId, "NameTextBox") }
        };
    }

    [Theory]
    [MemberData(nameof(SnapshotAwareMutationWrappers))]
    public async Task SnapshotAwareMutationWrappers_WithActiveSnapshot_ShouldRecommendStateDiff(
        string toolName,
        Func<SessionManager, Task<CallToolResult>> invoke)
    {
        AddActiveSnapshot();

        var result = await invoke(_sessionManager);

        var structured = result.StructuredContent!.Value;
        var navigation = structured.GetProperty("navigation");
        navigation.GetProperty("recommended")
            .EnumerateArray()
            .Select(step => step.GetProperty("tool").GetString())
            .Should()
            .Contain("get_state_diff", $"{toolName} should use the real wrapper path to preserve snapshot-safe follow-up navigation");
        navigation.GetProperty("contextRefs")[0].GetProperty("snapshotId").GetString().Should().Be(SnapshotId);
    }

    private void AddActiveSnapshot()
    {
        DisableSessionManagerCleanupTimer(_sessionManager);
        _sessionManager.AddSession(ProcessId);
        _sessionManager.SaveStateSnapshot(ProcessId, new StoredStateSnapshot(
            SnapshotId,
            SnapshotName: null,
            ElementId: null,
            DependencyProperties: Array.Empty<StoredDependencyPropertySnapshot>(),
            ViewModelProperties: Array.Empty<StoredViewModelPropertySnapshot>(),
            Focus: null,
            BindingErrors: Array.Empty<StoredBindingErrorSnapshot>(),
            HasBindingErrorBaseline: true,
            ValidationErrors: Array.Empty<StoredValidationErrorSnapshot>(),
            HasValidationBaseline: true,
            DateTimeOffset.UtcNow));
        _sessionManager.SetActiveSnapshotId(ProcessId, SnapshotId);
    }
}
