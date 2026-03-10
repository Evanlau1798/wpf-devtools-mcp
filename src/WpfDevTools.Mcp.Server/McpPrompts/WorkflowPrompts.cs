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
        1. Call get_processes to list running WPF applications.
        2. Choose the target processId and call connect(processId).
        3. Call ping(processId) to confirm the Inspector session is healthy.
        4. Call get_windows(processId) to enumerate all windows.
        5. If a secondary window matters, pass its elementId into get_visual_tree or get_logical_tree.

        If connect fails with an elevated-target error, stop and restart the MCP server as administrator.
        """;

    [McpServerPrompt(Name = "debug_binding_issue", Title = "Debug Binding Issue")]
    [Description("Workflow prompt for binding failures, null DataContext, fallback values, and validation diagnostics.")]
    public static string DebugBindingIssue() =>
        """
        Goal: diagnose why WPF data is blank, stale, or incorrect.

        Recommended workflow:
        1. connect(processId)
        2. get_binding_errors(processId)
        3. get_visual_tree(processId, depth=3) to locate the affected elementId
        4. get_bindings(processId, elementId)
        5. get_binding_value_chain(processId, elementId, propertyName)
        6. get_datacontext_chain(processId, elementId)
        7. get_validation_errors(processId, elementId) when validation may be involved

        Prefer these tools as a set. They describe different layers of the same binding story.
        """;

    [McpServerPrompt(Name = "debug_command_or_click", Title = "Debug Command Or Click")]
    [Description("Workflow prompt for disabled buttons, CanExecute issues, click routing, and event-handler inspection.")]
    public static string DebugCommandOrClick() =>
        """
        Goal: understand why a button, menu item, or clickable control does not respond.

        Recommended workflow:
        1. capture_state_snapshot(processId, elementId, includeFocus=true) if you need a clean rollback point
        2. connect(processId)
        3. get_dp_value_source(processId, elementId, propertyName='IsEnabled')
        4. get_commands(processId, elementId)
        5. get_event_handlers(processId, elementId, eventName='Click')
        6. trace_routed_events(processId, elementId, eventName='Click', mode='start')
        7. click_element(processId, elementId)
        8. trace_routed_events(processId, mode='get')

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

    [McpServerPrompt(Name = "inspect_secondary_window", Title = "Inspect Secondary Window")]
    [Description("Workflow prompt for dialogs, tool windows, popups, and focus-sensitive multi-window flows.")]
    public static string InspectSecondaryWindow() =>
        """
        Goal: inspect a non-main WPF window without accidentally targeting the wrong root.

        Recommended workflow:
        1. connect(processId)
        2. get_windows(processId)
        3. get_focus_state(processId)
        4. Select the desired window by title, type, isVisible, or isMainWindow
        5. Use that window elementId with get_visual_tree, get_logical_tree, screenshot, or interaction tools
        6. Re-check get_windows if focus or visibility changes during the flow

        Never assume omitted elementId means the currently focused window. It means Application.MainWindow.
        """;
}
