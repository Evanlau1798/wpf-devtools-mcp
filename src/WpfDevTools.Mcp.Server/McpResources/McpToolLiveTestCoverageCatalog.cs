namespace WpfDevTools.Mcp.Server.McpResources;

internal static class McpToolLiveTestCoverageCatalog
{
    private const string Missing = "missing";
    private const string LiveE2eCovered = "live-e2e-covered";

    private static readonly HashSet<string> LiveE2eCoveredTools = new(StringComparer.Ordinal)
    {
        "batch_mutate",
        "capture_state_snapshot",
        "clear_dp_value",
        "click_element",
        "connect",
        "diagnose_visibility",
        "drag_and_drop",
        "drain_events",
        "element_screenshot",
        "execute_command",
        "find_binding_leaks",
        "find_elements",
        "fire_routed_event",
        "focus_element",
        "get_active_process",
        "get_affected_elements",
        "get_applied_styles",
        "get_binding_errors",
        "get_binding_mismatches",
        "get_binding_value_chain",
        "get_bindings",
        "get_commands",
        "get_datacontext_chain",
        "get_dp_value_source",
        "get_element_snapshot",
        "get_form_summary",
        "get_interaction_readiness",
        "get_layout_info",
        "get_logical_tree",
        "get_namescope",
        "get_processes",
        "get_render_stats",
        "get_state_diff",
        "get_ui_summary",
        "get_viewmodel",
        "get_visual_count",
        "get_visual_tree",
        "get_windows",
        "measure_element_render_time",
        "modify_viewmodel",
        "ping",
        "restore_state_snapshot",
        "select_active_process",
        "set_dp_value",
        "simulate_keyboard",
        "trace_routed_events",
        "wait_for_dp_change",
        "wait_for_dp_change_after_mutation",
        "watch_dp_changes"
    };

    public static string GetStatus(string toolName)
        => LiveE2eCoveredTools.Contains(toolName)
            ? LiveE2eCovered
            : Missing;
}
