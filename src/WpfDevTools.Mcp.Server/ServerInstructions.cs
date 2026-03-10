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
        WPF DevTools MCP Server: Deep inspection and interaction with running WPF applications via in-process DLL injection. Provides process discovery, visual/logical tree inspection, binding diagnostics, MVVM debugging, DependencyProperty analysis, style/template inspection, routed event diagnostics, element interaction, layout analysis, and performance profiling.

        === MANDATORY WORKFLOW ===
        1. get_processes -> discover running WPF apps and their processIds
        2. connect(processId) -> inject Inspector DLL; MUST succeed before any other tool
        3. Use inspection/interaction tools with the same processId

        === PARAMETER CONVENTIONS ===
        - processId: integer, from get_processes, required by all tools except get_processes
        - nameFilter: string, optional on get_processes, case-insensitive substring match on process name
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
        - For multi-window apps: call get_windows first to discover all windows and their elementId values

        === MULTI-WINDOW SUPPORT ===
        - Call get_windows to discover all open windows in the connected process
        - Each window returns an elementId that can be used in any tool
        - Default root (when elementId is omitted) is always Application.MainWindow
        - To inspect a secondary window: get_windows -> use returned elementId as elementId parameter
        - Cross-window element search: tools automatically search all windows if element not found in main window

        === TOOL SEARCH ===
        - Clients with many MCP tools may rely on tool search instead of loading every tool eagerly
        - Prefer tool Title + Description matching over raw tool-name guessing
        - Process discovery keywords: process, connect, session, WPF app
        - Tree keywords: visual tree, logical tree, namescope, template, windows
        - Binding keywords: binding, DataContext, validation, value chain
        - Interaction keywords: click, keyboard, screenshot, drag, scroll
        - Runtime metadata is returned as structured JSON; use structured fields over text scraping when possible

        === TOOL SELECTION GUIDE ===
        - Blank screen / wrong data? -> get_binding_errors, get_bindings, get_datacontext_chain
        - UI not responding to changes? -> get_dp_value_source, get_viewmodel
        - Button disabled/not working? -> get_commands (CanExecute), get_event_handlers
        - Button click not working? -> click_element (full pipeline with ICommand) or fire_routed_event (event-handler-only for non-ButtonBase; OnClick path for ButtonBase+Click)
        - Form validation errors? -> get_validation_errors (aggregates ALL descendant errors recursively)
        - Layout broken? -> get_layout_info (size), get_clipping_info (overflow)
        - Style not applied? -> get_applied_styles, get_resource_chain
        - Performance slow? -> get_visual_count, get_render_stats, find_binding_leaks
        - Multiple windows / dialogs? -> get_windows to discover all windows, use elementId to target specific window

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
        - In STDIO transport, prefer polling workflows over push-style watcher/event streaming expectations
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

        Workflow 5 - Multi-Window Inspection:
        get_processes -> connect -> get_windows -> get_visual_tree(elementId=<windowElementId>, depth=3) -> inspect subtree

        Workflow 6 - Debug Event Handling:
        get_processes -> connect -> get_event_handlers(elementId, eventName) -> trace_routed_events(mode="start", eventName) -> click_element(elementId) -> trace_routed_events(mode="get")

        Workflow 7 - Debug Validation Errors:
        get_processes -> connect -> get_validation_errors() [root-scope aggregates all descendants] -> get_validation_errors(elementId) [narrow to specific element] -> get_bindings(elementId)

        === NORMALIZATION & CONTRACT NOTES ===
        Some tools apply automatic normalization. Responses include metadata so you can see what was normalized:
        - trace_routed_events (start mode): enforces minimum 30s duration for AI agent IPC round-trips. Response includes both requestedDuration and effectiveDuration.
        - fire_routed_event: for ButtonBase + Click event, uses OnClick() path instead of RaiseEvent(). Response includes usedOnClick: true.
        - get_validation_errors: recursively aggregates errors from ALL visual descendants (max depth: 50, max errors: 200). Each error includes elementType/elementName to identify the source.
        - set_dp_value / modify_viewmodel: JSON string values with surrounding double-quotes are auto-stripped (e.g., "\"hello\"" becomes "hello").

        === ERROR RECOVERY ===
        - "not connected" -> call connect(processId) first, then retry
        - "Access denied" (errorCode: AccessDenied) -> restart MCP server as administrator
        - elevated target processes may be discoverable but still require the MCP server itself to run as administrator before connect/click/mutation tools can succeed
        - "Not a WPF application" -> use get_processes to find correct processId
        - "Architecture mismatch" -> MCP server and target process must have matching architectures (both x64 or both x86). AnyCPU Inspector DLLs are auto-detected and compatible with any platform, but the injection mechanism still requires matching bitness between server and target
        - "signature verification failed" (errorCode: SecurityError) -> use a Debug build for local development (auto-skips verification for local DLLs), or sign the Inspector DLL with Authenticode for production
        - "timeout" -> process may be frozen; try ping() to verify connection
        - "element not found" -> verify elementId from get_visual_tree/get_logical_tree
        - "property not found" -> verify propertyName spelling and element type
        - "Rate limit exceeded" -> wait 1 minute, then retry. Response includes { availableTokens, retryAfterSeconds: 60 }
        - errorCode "InternalError" -> an unexpected server error; retry or report the issue
        - errorCode "FileNotFound" -> required file is missing; verify build output
        - errorCode "OperationError" -> operation failed; check error message for details

        === RESPONSE FORMAT ===
        All tools return JSON: { success: boolean, ...fields }
        On error: { success: false, error: string, errorCode?: string, errorData?: object }
        - errorCode is the Inspector error enum name when the request reached the in-process Inspector
        - errorData is optional structured context for automated recovery logic

        === LIMITATIONS ===
        - STDIO transport: Cannot push live watcher/event streams; use request-response diagnostics and polling workflows
        - Self-contained single-file apps and Native AOT apps: Cannot inject (use SDK mode)
        - elevated targets: a non-administrator MCP server cannot inject into or control an administrator-launched WPF process
        - Changes are NOT persisted to XAML files
        """;
}
