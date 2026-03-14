using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

internal sealed record BatchMutationRequest(
    int ProcessId,
    string? DefaultElementId,
    IReadOnlyList<BatchMutationStep> Mutations,
    BatchMutationSnapshot? CaptureSnapshot,
    bool IncludeDiff,
    string DiffTrigger);

internal sealed record BatchMutationStep(
    int Index,
    string Tool,
    JsonElement Args,
    string? Label);

internal sealed record BatchMutationSnapshot(JsonElement Args);

internal static class BatchMutationCatalog
{
    internal static readonly IReadOnlySet<string> SupportedTools = new HashSet<string>(StringComparer.Ordinal)
    {
        "modify_viewmodel",
        "set_dp_value",
        "clear_dp_value",
        "execute_command",
        "click_element",
        "fire_routed_event",
        "focus_element",
        "scroll_to_element",
        "simulate_keyboard",
        "override_style_setter",
        "drag_and_drop"
    };
}
