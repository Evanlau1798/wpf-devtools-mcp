using System.ComponentModel;
using ModelContextProtocol.Server;

namespace WpfDevTools.Mcp.Server.McpPrompts;

[McpServerPromptType]
public static class WorkflowPrompts
{
    [McpServerPrompt(Name = "connect_and_list_windows", Title = "Connect And List Windows")]
    [Description("Workflow prompt for process discovery, connection, ping, and multi-window enumeration.")]
    public static string ConnectAndListWindows() =>
        """
        Goal: connect to a WPF process and discover all open windows.

        Recommended workflow:
        1. Call connect() first; let the server auto-discover the target when there is only one visible WPF app.
        2. Do not call get_processes before connect() unless auto-discovery is ambiguous or you explicitly need filtered process discovery.
        3. If connect() returns multiple candidates, call get_processes(windowFilter='visible' or 'all'), choose the target processId, and retry connect(processId).
        4. Prefer get_ui_summary before expanding full trees for a specific window.
        5. Call get_windows(processId) to enumerate all windows.
        6. If a secondary window matters, pass its elementId into get_visual_tree or get_logical_tree.
        7. Call ping(processId) only if you need an explicit Inspector health check.

        If connect fails with an elevated-target error, stop and restart the MCP server as administrator.
        """;

    [McpServerPrompt(Name = "debug_binding_issue", Title = "Debug Binding Issue")]
    [Description("Workflow prompt for binding failures, null DataContext, fallback values, and validation diagnostics.")]
    public static string DebugBindingIssue() =>
        """
        Goal: diagnose why WPF data is blank, stale, or incorrect.

        Recommended workflow:
        1. connect()
        2. get_binding_errors()
        3. Follow navigation.recommended or nextSteps from the latest diagnostic result
        4. If navigation is absent, or the failing element is already known, call get_element_snapshot(elementId) for one-call local context
        5. get_bindings(elementId)
        6. get_binding_value_chain(elementId, propertyName)
        7. get_datacontext_chain(elementId)
        8. get_validation_errors(elementId) when validation may be involved

        Prefer navigation.recommended first. Use the remaining tools when the next step still needs clarification across binding source, value chain, or validation state.
        """;

    [McpServerPrompt(Name = "debug_command_or_click", Title = "Debug Command Or Click")]
    [Description("Workflow prompt for disabled buttons, CanExecute issues, click routing, and event-handler inspection.")]
    public static string DebugCommandOrClick() =>
        """
        Goal: understand why a button, menu item, or clickable control does not respond.

        Recommended workflow:
        1. connect()
        2. capture_state_snapshot(elementId, includeFocus=true) if you need a clean rollback point
        3. get_interaction_readiness(elementId, interactionType='Click')
        4. get_commands(elementId)
        5. get_event_handlers(elementId, eventName='Click')
        6. trace_routed_events(elementId, eventName='Click', mode='start')
        7. click_element(elementId)
        8. trace_routed_events(mode='get')
        9. get_state_diff(snapshotId, trigger='click_element(...)') when you need to summarize what changed

        If the control is disabled, diagnose CanExecute or style/trigger state before forcing interaction.
        """;

    [McpServerPrompt(Name = "diagnose_elevated_target", Title = "Diagnose Elevated Target")]
    [Description("Workflow prompt for administrator-launched targets, access denied errors, and transport limitations.")]
    public static string DiagnoseElevatedTarget() =>
        """
        Goal: determine whether an administrator-launched WPF process can be controlled from the current MCP session.

        Recommended workflow:
        1. get_processes and inspect isElevated / requiresElevationToConnect
        2. connect(processId)
        3. If connect returns AccessDenied for an elevated target, restart the MCP server with administrator rights
        4. Re-run connect and then ping

        Key rule:
        - A non-administrator MCP server can discover an elevated target, but it cannot inject into or control it.
        - In stdio mode, the MCP server inherits the host client's privilege level.
        """;

    [McpServerPrompt(Name = "profile_performance", Title = "Profile Performance")]
    [Description("Workflow prompt for visual count, render stats, binding leak detection, and element render measurement.")]
    public static string ProfilePerformance() =>
        """
        Goal: identify performance bottlenecks in the WPF application.

        Recommended workflow:
        1. connect()
        2. get_visual_count() to understand the total visual tree size
        3. get_render_stats() for frame timing and render metrics (first call may return zeros; call again after a short wait)
        4. find_binding_leaks(threshold=50) to detect binding references that may indicate memory leaks
        5. If a specific element is suspected: measure_element_render_time(elementId) for targeted profiling
        6. Use get_ui_summary to correlate heavy subtrees with their visual count

        Key notes:
        - get_render_stats requires the Inspector to have monitored at least one render cycle; a warm-up call is normal.
        - Avoid calling performance tools in tight loops; use one-shot polling instead.
        - Performance metrics are session-scoped and reset on reconnect.
        """;

    [McpServerPrompt(Name = "inspect_secondary_window", Title = "Inspect Secondary Window")]
    [Description("Workflow prompt for dialogs, tool windows, popups, and focus-sensitive multi-window flows.")]
    public static string InspectSecondaryWindow() =>
        """
        Goal: inspect a non-main WPF window without accidentally targeting the wrong root.

        Recommended workflow:
        1. connect()
        2. If connect() reports multiple candidates, call get_processes(windowFilter) and retry connect(processId)
        3. get_windows(processId)
        4. get_focus_state(processId)
        5. Select the desired window by title, type, isVisible, or isMainWindow
        6. Use that window elementId with get_ui_summary, get_visual_tree, get_logical_tree, screenshot, or interaction tools
        7. Re-check get_windows if focus or visibility changes during the flow

        Never assume omitted elementId means the currently focused window. It means Application.MainWindow.
        """;
}
