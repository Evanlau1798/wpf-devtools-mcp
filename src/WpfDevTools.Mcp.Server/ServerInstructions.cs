namespace WpfDevTools.Mcp.Server;

using WpfDevTools.Mcp.Server.Schema;

/// <summary>
/// Server instructions sent to MCP clients during initialization.
/// Provides comprehensive guidance for AI agents on tool usage, workflows, and error recovery.
/// </summary>
public static class ServerInstructions
{
    /// <summary>
    /// The complete server instructions text
    /// </summary>
    public static readonly string Value = $$"""
        WPF DevTools MCP Server inspects and debugs a running WPF application through in-process runtime diagnostics and interaction tools.
        Search this server when you need to inspect a running WPF application, its visual tree, logical tree, binding failures, ViewModel state, dependency properties, commands, focus, performance, secondary windows, or exact runtime element matches.
        Use this server for runtime desktop UI diagnostics, WPF element lookup, exact-match element search, XAML structure inspection, multi-window investigation, and safe temporary automation against a connected WPF process.

        === MANDATORY WORKFLOW ===
        1. connect() -> try auto-discovery against visible WPF apps and inject Inspector DLL
        2. If connect() reports multiple candidates, call get_processes(windowFilter) and retry connect(processId)
        3. Build initial context with get_ui_summary, get_element_snapshot, or get_form_summary before expanding trees
        4. Use focused inspection/interaction tools against the connected process
        5. Do not call get_processes before connect() unless auto-discovery is ambiguous or you explicitly need filtered discovery before connecting

        === PARAMETER CONVENTIONS ===
        - processId: integer, from get_processes, optional after connect()/select_active_process() establishes the active process
        - windowFilter: string, one of 'visible' (default), 'all', or 'foreground' for process discovery and auto-discovery narrowing
        - nameFilter: string, optional on get_processes, case-insensitive substring match on process name
        - elementId: string, from get_visual_tree/get_logical_tree, optional (omit = root window)
        - depth: integer (1-100), controls tree traversal depth, default=10
        - propertyName: string, DependencyProperty name (e.g., 'Text', 'IsEnabled')
        - commandName: string, ICommand property name (e.g., 'SaveCommand')
        - eventName: string, WPF RoutedEvent name (e.g., 'Click', 'MouseDown')
        - resourceKey: string, XAML resource key (e.g., 'PrimaryBrush')
        - snapshotId: string, returned by capture_state_snapshot for later restore

        === TIMEOUTS ===
        - connect(): 30 seconds (DLL injection + IPC handshake)
        - ping(): 5 seconds
        - All other tools: 5 seconds (UI thread operations)
        - If timeout occurs, process may be frozen or unresponsive

        === RATE LIMITS ===
        - Per-session: 300 requests/minute per connected process
        - Tree tools: Use depth parameter to limit response size
        - Performance tools: Avoid calling in tight loops

        === ELEMENT DISCOVERY ===
        - elementId is required by many tools; omitting it targets the root window
        - First call find_elements when you need a compact lookup by type/name/automationId/property value
        - Then call get_visual_tree or get_logical_tree when you need the surrounding structure
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
        - MCP prompts may surface as slash commands in compatible clients; use them as workflow entry points
        - MCP resources may surface through @resource references in compatible clients; use them for capability and limitation summaries
        - Process discovery keywords: process, connect, session, WPF app
        - Tree keywords: visual tree, logical tree, namescope, template, windows, find elements, search element
        - Binding keywords: binding, DataContext, validation, value chain
        - Interaction keywords: click, keyboard, screenshot, drag, scroll
        - State keywords: snapshot, restore, rollback, focus
        - Runtime metadata is returned as structured JSON; use structured fields over text scraping when possible

        === TOOL SELECTION GUIDE ===
        - Need quick scene context first? -> get_ui_summary (semantic default; use summaryOnly=true when you only need summaryText), get_element_snapshot, or get_form_summary
        - Need exact element lookup first? -> find_elements, then get_visual_tree/get_logical_tree for local structure
        - Blank screen / wrong data? -> get_binding_errors, get_bindings, get_datacontext_chain
        - Binding active but data looks wrong? -> get_binding_mismatches (path, type, nullability analysis)
        - UI not responding to changes? -> get_dp_value_source, get_viewmodel
        - Element exists but not visible? -> diagnose_visibility (checks Visibility, Opacity, size, clipping)
        - Button disabled/not working? -> get_commands (CanExecute), get_event_handlers
        - Button click not working? -> click_element (full pipeline with ICommand) or fire_routed_event (event-handler-only for non-ButtonBase; OnClick path for ButtonBase+Click)
        - Form validation errors? -> get_validation_errors (aggregates ALL descendant errors recursively)
        - Layout broken? -> get_layout_info (size), get_clipping_info (overflow)
        - Style not applied? -> get_applied_styles, get_resource_chain
        - Performance slow? -> get_visual_count, get_render_stats, find_binding_leaks
        - Focus-related issues? -> get_focus_state (current focus), focus_element (move focus)
        - Multiple windows / dialogs? -> get_windows to discover all windows, use elementId to target specific window
        - Need safe rollback before debugging? -> capture_state_snapshot before mutation, restore_state_snapshot afterwards

        === TOKEN EFFICIENCY ===
        - Use depth=1 for immediate children only (fastest)
        - Use depth=2-3 for typical UI exploration (recommended)
        - Use depth=5+ only when deep tree analysis is necessary
        - Use elementId to scope tools to a subtree instead of full tree
        - Use nameFilter on get_processes to reduce response size

        === AI AGENT BEST PRACTICES ===
        - Start with connect() unless you already know you need a specific processId or non-default windowFilter
        - When connect() reports multiple candidates, use get_processes(windowFilter) to disambiguate and retry
        - Do not call get_processes before connect() as a default habit; treat it as a disambiguation or filtering tool
        - Store processId in conversation context after successful connect()
        - Prefer get_ui_summary/get_element_snapshot/get_form_summary before tree-heavy inspection
        - Prefer get_ui_summary(summaryOnly=true, depthMode='semantic') for initial scene orientation unless you already have a narrow element-centric question
        - Use depth=2-3 for initial tree exploration; increase only if needed
        - Batch related operations in single turn (e.g., get_visual_tree + get_bindings)
        - Prefer slash commands from MCP prompts when you want a predefined workflow entry point
        - Prefer @resource lookups when you need capability summaries, workflow references, or known limitations before acting
        - Check IsEnabled with get_dp_value_source before click_element to avoid errors
        - Use get_binding_errors as first diagnostic step for data display issues; its default response is compact, so pass compact=false only when the full human-readable binding trace text is required
        - Treat returned runtime navigation as session-aware when you have already captured a snapshot or started a routed-event trace in the same connected process
        - When you already know the next step and do not need server guidance, pass navigation=false to omit navigation and compatibility nextSteps on that specific call
        - In STDIO transport, prefer polling workflows over push-style watcher/event streaming expectations
        - Avoid calling performance tools (get_render_stats, measure_element_render_time) in loops
        - When debugging, start broad (get_binding_errors) then narrow (get_bindings on specific element)
        - For MVVM apps, inspect ViewModel first (get_viewmodel, get_commands) before modifying
        - If get_dp_value_source reports isExpression=true, treat set_dp_value as a temporary override that replaces the existing expression until the control is rebound by app logic
        - Remember: all destructive changes are runtime-only and NOT persisted to XAML

        === DESTRUCTIVE TOOLS (modify running app - changes NOT persisted to XAML) ===
        - set_dp_value, clear_dp_value, override_style_setter: change property/style values
        - modify_viewmodel: change ViewModel properties
        - execute_command, fire_routed_event, click_element, simulate_keyboard: trigger actions
        - drag_and_drop: simulate drag-drop operations
        - invalidate_layout: force layout recalculation
        - focus_element, restore_state_snapshot: change focus or replay captured runtime state

        === COMMON WORKFLOWS ===

        Workflow 1 - Debug Binding Error:
        connect() -> get_binding_errors -> follow navigation.recommended -> get_element_snapshot(elementId) -> get_bindings(elementId) -> get_datacontext_chain(elementId)

        Workflow 2 - Test Button Click:
        connect() -> capture_state_snapshot(processId, elementId, propertyNames/viewModelPropertyNames, includeFocus=true) -> get_form_summary or get_ui_summary -> get_interaction_readiness(elementId, 'Click') -> click_element(elementId) -> get_state_diff(snapshotId)

        Workflow 3 - Inspect ViewModel:
        connect() -> get_element_snapshot(elementId) -> get_viewmodel(elementId) -> get_commands(elementId) -> modify_viewmodel(elementId, propertyName, value)

        Workflow 4 - Performance Profiling:
        connect() -> get_processes(windowFilter) only if connect() reports multiple candidates -> get_visual_count -> get_render_stats -> find_binding_leaks(threshold=50) -> measure_element_render_time(elementId)

        Workflow 5 - Multi-Window Inspection:
        connect() -> get_windows -> get_ui_summary(elementId=<windowElementId>, depthMode='semantic') -> get_visual_tree(elementId=<windowElementId>, depth=3) -> inspect subtree

        Workflow 6 - Debug Event Handling:
        connect() -> get_processes(windowFilter) only if connect() reports multiple candidates -> get_event_handlers(elementId, eventName) -> trace_routed_events(mode="start", eventName) -> click_element(elementId) -> trace_routed_events(mode="get")

        Workflow 7 - Debug Validation Errors:
        connect() -> get_form_summary -> get_validation_errors() [root-scope aggregates all descendants] -> get_validation_errors(elementId) [narrow to specific element] -> get_bindings(elementId)

        Workflow 8 - Safe Mutation Session:
        connect() -> capture_state_snapshot(processId, elementId, propertyNames/viewModelPropertyNames, includeFocus=true) -> perform runtime mutations -> get_state_diff(snapshotId, trigger='...') -> restore_state_snapshot(processId, snapshotId)

        === NORMALIZATION & CONTRACT NOTES ===
        Some tools apply automatic normalization. Responses include metadata so you can see what was normalized:
        - trace_routed_events (start mode): enforces minimum 30s duration for AI agent IPC round-trips. Response includes both requestedDuration and effectiveDuration.
        - fire_routed_event: for ButtonBase + Click event, uses OnClick() path instead of RaiseEvent(). Response includes usedOnClick: true.
        - get_validation_errors: recursively aggregates errors from ALL visual descendants (max depth: 50, max errors: 200). Each error includes elementType/elementName to identify the source.
        - set_dp_value / modify_viewmodel: JSON string values with surrounding double-quotes are auto-stripped (e.g., "\"hello\"" becomes "hello").
        - set_dp_value reports replacedExpression=true when it overwrote an expression-backed DependencyProperty with a local value.
        - restore_state_snapshot verifies expression-backed DependencyProperties but reports them as skipped instead of claiming it can rebuild the original expression object.
        - binding/data-context/validation diagnostics expose normalized diagnosticKind/sourceKind fields for cross-tool correlation.
        - get_binding_errors defaults to compact output at the MCP contract layer while preserving server-side follow-up guidance; use compact=false when you need the original verbose message text.
        - mutation and interaction tools default to detail=compact when you want trimmed additive normalization metadata while preserving the same core semantics.
        - use detail=minimal when a mutation workflow only needs success/property/newValue confirmation.
        - use detail=verbose when you need requested/effective input plus observedEffect metadata; legacy detail=standard remains accepted as a compatibility alias.

        === RESPONSE CONTRACT VERSION ===
        - Current response contract version: {{ResponseContractVersion.Current}}
        - Every tool response includes the additive `navigation` envelope; prefer `navigation.recommended` as the preferred follow-up surface when present instead of ad hoc tool guessing.
        - Every tool response also includes compatibility `nextSteps`; expect `nextSteps: []` when the server has no deterministic runtime guidance.
        - v2 `nextSteps` entries may also include optional `preconditions`, `expectedOutcome`, `workflowId`, and `prefetchTools` fields.
        - v3 `navigation` includes `recommended`, `alternatives`, `prefetchTools`, and descriptive `contextRefs` entries.
        - `nextSteps` remains a compatibility field and is derived from `navigation.recommended` for clients that ignore the richer envelope.
        - Clients may use `navigation=false` as an explicit opt-out to omit both `navigation` and compatibility `nextSteps` on that specific call.
        - These optional fields are session-aware hints for capable clients; older clients can ignore them safely.
        - `workflowId` and `expectedOutcome` are advisory only and describe short verification loops, not executable server-side orchestration.
        - `prefetchTools` is advisory only and contains tool names, not parameters or hidden commands.
        - `contextRefs` entries are descriptive JSON only; they are not opaque handles and must not be treated as implicit tool execution requests.
        - Compatibility aliases remain available in the current contract for backward compatibility.
        - Compatibility aliases:
          - currentValue -> effectiveValue
          - typeName -> viewModelType
          - avgRenderTime -> averageFrameTime
          - count -> totalCount
          - renderTimeMs -> renderTime

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
        - "Rate limit exceeded" -> wait the returned `retryAfterSeconds`, then retry. Response includes { availableTokens, retryAfterSeconds, retryAfter }
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
