namespace WpfDevTools.Mcp.Server;

/// <summary>
/// Server instructions sent to MCP clients during initialization.
/// Provides comprehensive guidance for AI agents on tool usage, workflows, and error recovery.
/// </summary>
public static class ServerInstructions
{
    /// <summary>
    /// The complete server instructions text
    /// </summary>
    public const string Value = """
        WPF DevTools MCP Server: Deep inspection and interaction with running WPF applications via in-process DLL injection. Provides 44 tools across 10 categories for Visual Tree inspection, Binding diagnostics, MVVM debugging, DependencyProperty analysis, Style/Template inspection, RoutedEvent tracing, element interaction, layout analysis, and performance profiling.

        === MANDATORY WORKFLOW ===
        1. get_processes -> discover running WPF apps and their processIds
        2. connect(processId) -> inject Inspector DLL; MUST succeed before any other tool
        3. Use inspection/interaction tools with the same processId

        === PARAMETER CONVENTIONS ===
        - processId: integer, from get_processes, required by all tools except get_processes
        - elementId: string, from get_visual_tree/get_logical_tree, optional (omit = root window)
        - depth: integer (1-100), controls tree traversal depth, default=10
        - propertyName: string, DependencyProperty name (e.g., 'Text', 'IsEnabled')
        - commandName: string, ICommand property name (e.g., 'SaveCommand')
        - eventName: string, WPF RoutedEvent name (e.g., 'Click', 'MouseDown')
        - resourceKey: string, XAML resource key (e.g., 'PrimaryBrush')

        === TIMEOUTS ===
        - connect(): 30 seconds (DLL injection + IPC handshake)
        - ping(): 5 seconds
        - All other tools: 5 seconds (UI thread operations)
        - If timeout occurs, process may be frozen or unresponsive

        === RATE LIMITS ===
        - Per-session: 100 requests/minute per connected process
        - Tree tools: Use depth parameter to limit response size
        - Performance tools: Avoid calling in tight loops

        === ELEMENT DISCOVERY ===
        - elementId is required by many tools; omitting it targets the root window
        - First call get_visual_tree or get_logical_tree to discover elementId values
        - Each tree node returns an elementId field - use these in subsequent tool calls
        - elementId format: 'TypeName_N' (e.g., 'Button_1', 'TextBox_5') - stable per session

        === TOOL SELECTION GUIDE ===
        - Blank screen / wrong data? -> get_binding_errors, get_bindings, get_datacontext_chain
        - UI not responding to changes? -> get_dp_value_source, get_viewmodel
        - Button disabled/not working? -> get_commands (CanExecute), get_event_handlers
        - Layout broken? -> get_layout_info (size), get_clipping_info (overflow)
        - Style not applied? -> get_applied_styles, get_resource_chain
        - Performance slow? -> get_visual_count, get_render_stats, find_binding_leaks

        === TOKEN EFFICIENCY ===
        - Use depth=1 for immediate children only (fastest)
        - Use depth=2-3 for typical UI exploration (recommended)
        - Use depth=5+ only when deep tree analysis is necessary
        - Use elementId to scope tools to a subtree instead of full tree
        - Use nameFilter on get_processes to reduce response size

        === AI AGENT BEST PRACTICES ===
        - Always call get_processes first to discover available WPF apps
        - Store processId in conversation context after successful connect()
        - Use depth=2-3 for initial tree exploration; increase only if needed
        - Batch related operations in single turn (e.g., get_visual_tree + get_bindings)
        - Check IsEnabled with get_dp_value_source before click_element to avoid errors
        - Use get_binding_errors as first diagnostic step for data display issues
        - Avoid calling performance tools (get_render_stats, measure_element_render_time) in loops
        - When debugging, start broad (get_binding_errors) then narrow (get_bindings on specific element)
        - For MVVM apps, inspect ViewModel first (get_viewmodel, get_commands) before modifying
        - Remember: all destructive changes are runtime-only and NOT persisted to XAML

        === DESTRUCTIVE TOOLS (modify running app - changes NOT persisted to XAML) ===
        - set_dp_value, clear_dp_value, override_style_setter: change property/style values
        - modify_viewmodel: change ViewModel properties
        - execute_command, fire_routed_event, click_element, simulate_keyboard: trigger actions
        - drag_and_drop: simulate drag-drop operations
        - invalidate_layout: force layout recalculation

        === COMMON WORKFLOWS ===

        Workflow 1 - Debug Binding Error:
        get_processes -> connect -> get_binding_errors -> get_visual_tree(depth=3) -> get_datacontext_chain(elementId) -> get_bindings(elementId)

        Workflow 2 - Test Button Click:
        get_processes -> connect -> get_visual_tree(depth=2) -> get_dp_value_source(elementId, 'IsEnabled') -> click_element(elementId)

        Workflow 3 - Inspect ViewModel:
        get_processes -> connect -> get_viewmodel -> get_commands -> modify_viewmodel(propertyName, value)

        Workflow 4 - Performance Profiling:
        get_processes -> connect -> get_visual_count -> get_render_stats -> find_binding_leaks(threshold=50) -> measure_element_render_time(elementId)

        === ERROR RECOVERY ===
        - "not connected" -> call connect(processId) first, then retry
        - "Access denied" -> restart MCP server as administrator
        - "Not a WPF application" -> use get_processes to find correct processId
        - "Architecture mismatch" -> ensure server and target app match (x64 vs x86); check architecture with get_processes, rebuild server for correct platform if needed
        - "timeout" -> process may be frozen; try ping() to verify connection
        - "element not found" -> verify elementId from get_visual_tree/get_logical_tree
        - "property not found" -> verify propertyName spelling and element type

        === RESPONSE FORMAT ===
        All tools return JSON: { success: boolean, ...fields }
        On error: { success: false, error: string }

        === LIMITATIONS ===
        - STDIO transport: Cannot push events (watch_dp_changes requires polling)
        - Self-contained single-file apps and Native AOT apps: Cannot inject (use SDK mode)
        - Changes are NOT persisted to XAML files
        """;
}
