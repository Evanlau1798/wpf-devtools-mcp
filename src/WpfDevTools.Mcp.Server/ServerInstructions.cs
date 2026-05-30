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
        1. Confirm WPFDEVTOOLS_MCP_ALLOWED_TARGETS contains the reviewed target's exact local absolute executable path; unset values fail closed with SecurityError, and malformed configured entries fail closed with InvalidPolicyConfiguration before connect() attaches
        2. connect() -> try auto-discovery against visible allowlisted WPF apps, then reuse a compatible existing SDK host or apply the raw-injection target policy before injecting Inspector DLL
        3. If connect() reports multiple candidates, call get_processes(windowFilter) and retry connect(processId)
        4. Build initial context with get_ui_summary or get_form_summary before expanding trees; use get_element_snapshot(elementId) only after a concrete elementId is known
        5. Use focused inspection/interaction tools against the connected process
        6. Do not call get_processes before connect() unless auto-discovery is ambiguous or you explicitly need filtered discovery before connecting
        Prefer get_ui_summary for scene-first orientation.

        === PARAMETER CONVENTIONS ===
        - processId: integer, from get_processes, optional after connect()/select_active_process() establishes the active process
        - windowFilter: string, one of 'visible' (default), 'all', or 'foreground' for process discovery and auto-discovery narrowing
        - nameFilter: string, optional on get_processes, case-insensitive substring match on process name
        - elementId: string, from get_visual_tree/get_logical_tree, optional (omit = root window)
        - depth: integer (0-100), controls traversal depth. Tree tools default to depth=10 when depth is omitted; get_ui_summary defaults to depth=3 and depthMode='semantic'.
        - propertyName: string, DependencyProperty name (e.g., 'Text', 'IsEnabled')
        - commandName: string, ICommand property name (e.g., 'SaveCommand')
        - eventName: string, WPF RoutedEvent name (e.g., 'Click', 'MouseDown')
        - resourceKey: string, XAML resource key (e.g., 'PrimaryBrush')
        - snapshotId: string, returned by capture_state_snapshot for later restore

        === TIMEOUTS ===
        - connect(): 30 seconds (DLL injection + IPC handshake)
        - ping(): 5 seconds
        - wait_for_dp_change and wait_for_dp_change_after_mutation: default bounded wait window is 5 seconds; larger timeoutMs values are supported and the server adds a small execution headroom above the requested polling budget
        - All other tools: 5 seconds (UI thread operations)
        - If timeout occurs, process may be frozen or unresponsive

        === RATE LIMITS ===
        - Per-session: 300 requests/minute per connected process
        - Tree tools: Use depth parameter to limit response size
        - Performance tools: Avoid calling in tight loops

        === ELEMENT DISCOVERY ===
        - elementId is session-specific; omitting it targets Application.MainWindow
        - Use find_elements for compact lookup, then get_visual_tree/get_logical_tree for surrounding structure
        - Each tree/window node returns an elementId; use get_windows first for dialogs, tool windows, or secondary windows

        === MULTI-WINDOW SUPPORT ===
        - Default root is Application.MainWindow; get_windows returns elementId values for other open windows
        - Scope tree, screenshot, and interaction tools with the returned window elementId when secondary windows matter

        === TOOL SEARCH ===
        - Clients with many MCP tools may rely on tool search instead of loading every tool eagerly
        - For large toolsets, defer loading specialized tools until the task scope is known; start with scene/process/binding summaries, then search for narrower tools
        - Compact starter workflow resource: wpf://workflows/starter-path
        - Prefer tool Title + Description matching over raw tool-name guessing
        - Treat prompt names and resource URIs as the portable discovery contract across clients before relying on client-specific UI affordances
        - MCP prompts may surface as slash commands in compatible clients; use that rendering as a convenience layer over the prompt name contract
        - MCP resources may surface through @resource references in compatible clients; use that rendering as a convenience layer over the resource URI contract
        - Process discovery keywords: process, connect, session, WPF app
        - Tree keywords: visual tree, logical tree, namescope, template, windows, find elements, search element
        - Binding keywords: binding, DataContext, validation, value chain
        - Interaction keywords: click, keyboard, screenshot, drag, scroll
        - State keywords: snapshot, restore, rollback, focus
        - Runtime metadata is returned as structured JSON; use structured fields over text scraping when possible

        === TOOL SELECTION GUIDE ===
        - Need quick scene context first? -> get_ui_summary (semantic default; use summaryOnly=true when you only need summaryText) or get_form_summary; use get_element_snapshot(elementId) only after a concrete elementId is known
        - Need exact element lookup first? -> find_elements, then get_visual_tree/get_logical_tree for local structure
        - Blank screen / wrong data? -> get_binding_errors, get_bindings, get_datacontext_chain
        - Binding active but data looks wrong? -> get_binding_mismatches (path, type, nullability analysis)
        - UI not responding to changes? -> get_dp_value_source, get_viewmodel (MVVM)
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
        - Prefer scene tools first: get_ui_summary defaults to depth=3 and depthMode='semantic'; use summaryOnly=true for orientation
        - Tree tools default to depth=10; initial tree exploration should usually pass depth=2-3 and scope by elementId
        - Use nameFilter on get_processes and compact=false/detail=verbose only when needed

        === AI AGENT BEST PRACTICES ===
        - Confirm WPFDEVTOOLS_MCP_ALLOWED_TARGETS contains the reviewed target's exact local absolute executable path before connect(); unset values fail closed with SecurityError, and malformed configured entries fail closed with InvalidPolicyConfiguration
        - Start with connect() after the target is allowlisted unless you already know you need a specific processId or non-default windowFilter
        - After connect() succeeds, immediately build context with get_ui_summary or get_form_summary before tree-heavy inspection or screenshots; use get_element_snapshot(elementId) only after a concrete elementId is known
        - When connect() reports multiple candidates, use get_processes(windowFilter) to disambiguate and retry
        - If connect() returns SecurityError with requiresExplicitTargetOptIn=true, prefer SDK-hosted reuse first. Only use WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS for explicitly reviewed external executables; malformed entries return InvalidPolicyConfiguration.
        - Do not call get_processes before connect() as a default habit; treat it as a disambiguation or filtering tool
        - When hidden or background targets matter, prefer connect(windowFilter='all') instead of listing processes first just to widen auto-discovery
        - When multiple WPF processes are expected and largest-target auto-selection is intentional, prefer connect(selectionStrategy='largest_working_set', windowFilter='all') instead of a separate get_processes round trip
        - run independent read-only inspections in parallel when the client supports it, then summarize intermediate tool results before choosing the next call
        - Keep only decision-relevant fields in conversation state; rely on elementId, snapshotId, and resource URI references for follow-up calls
        - complex parameters require concrete examples; use wpf://contracts/tool-examples and wpf://contracts/tools instead of invented shapes
        - Prefer prompt names and resource URIs as the portable discovery contract when you want a predefined workflow entry point or capability summary
        - Some clients surface those prompt/resource contracts as slash commands or @resource lookups; use that rendering only as a convenience layer
        - Check IsEnabled with get_dp_value_source before click_element; use get_binding_errors first for blank/stale data
        - Treat returned runtime navigation as session-aware when you have already captured a snapshot or started a routed-event trace in the same connected process
        - When you already know the next step and do not need server guidance, get_binding_errors accepts navigation=false to omit navigation and compatibility nextSteps on that specific call; schema-driven clients may rely on that opt-out there because the parameter is advertised in the tool schema today. Do not generalize that opt-out to other tools unless their schema explicitly advertises it too.
        - In STDIO transport, prefer polling workflows over push-style watcher/event streaming expectations
        - wait_for_dp_change is now read-only; if you need the old triggerMutation-style serialized mutation-plus-wait flow, use wait_for_dp_change_after_mutation instead
        - If get_dp_value_source reports isExpression=true, treat set_dp_value as a temporary override. Binding-backed expressions can usually be restored later in the same session with clear_dp_value or restore_state_snapshot; for two-way bindings, also snapshot the relevant ViewModel property when you need deterministic semantic rollback of the source value. Non-Binding expressions remain an explicit capability boundary
        - focus_element and simulate_keyboard require the target to be attached to the active rendered visual tree; activate inactive tabs before focus-sensitive actions
        - Remember: all destructive changes are runtime-only and NOT persisted to XAML

        === SERVER-SIDE POLICY GATES ===
        - Operators must configure WPFDEVTOOLS_MCP_ALLOWED_TARGETS with a semicolon-separated exact local absolute executable path allowlist before connect(); unset values fail closed with SecurityError, and malformed configured entries fail closed with InvalidPolicyConfiguration
        - WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS=true opts into runtime mutation, interaction, render-measurement, and session state-consuming tools such as capture_state_snapshot and drain_events before they reach the target process
        - WPFDEVTOOLS_MCP_ALLOW_SCREENSHOTS=true opts into element_screenshot at the MCP boundary
        - WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS=true opts into target UI text, DependencyProperty and binding values, routed-event payloads, tree/scene summaries, and runtime state snapshots such as get_ui_summary, get_visual_tree, get_bindings, and get_state_diff
        - WPFDEVTOOLS_MCP_ALLOW_VIEWMODEL_INSPECTION=true opts into get_viewmodel, get_commands, modify_viewmodel, and execute_command
        - Unset or disabled gates return errorCode: SecurityError; invalid boolean gate values return errorCode: InvalidPolicyConfiguration

        === DESTRUCTIVE TOOLS (modify running app or consume session state - changes NOT persisted to XAML) ===
        - set_dp_value, clear_dp_value, override_style_setter: change property/style values
        - modify_viewmodel: change ViewModel properties
        - execute_command, fire_routed_event, click_element, simulate_keyboard: trigger actions
        - drag_and_drop: simulate drag-drop operations
        - invalidate_layout: force layout recalculation
        - focus_element, restore_state_snapshot: change focus or replay captured runtime state
        - capture_state_snapshot, drain_events: create/replace session snapshots or consume buffered runtime events
        - wait_for_dp_change_after_mutation: executes one live mutation before waiting for the resulting property transition
        - batch_mutate: execute multiple mutations in ordered sequence
        - scroll_to_element: change scroll position to bring element into view
        - highlight_element: add temporary visual overlay adorner
        - measure_element_render_time: force bounded render passes for timing measurement

        === COMMON WORKFLOWS ===

        All common workflows assume WPFDEVTOOLS_MCP_ALLOWED_TARGETS already contains the reviewed target's exact local absolute executable path; unset values fail closed with SecurityError, and malformed configured entries fail closed with InvalidPolicyConfiguration before connect() attaches.

        Workflow 1 - Debug Binding Error:
        connect() -> get_binding_errors -> follow navigation.recommended -> get_element_snapshot(elementId) -> get_bindings(elementId) -> get_datacontext_chain(elementId)

        Workflow 2 - Test Button Click:
        connect() -> capture_state_snapshot(processId, elementId, propertyNames/viewModelPropertyNames, includeFocus=true) -> get_form_summary or get_ui_summary -> get_interaction_readiness(elementId, 'Click') -> click_element(elementId) -> get_state_diff(snapshotId) -> restore_state_snapshot(snapshotId)

        Workflow 3 - Inspect ViewModel:
        connect() -> get_element_snapshot(elementId) -> get_viewmodel(elementId) -> get_commands(elementId) -> modify_viewmodel(elementId, propertyName, value)

        Workflow 4 - Performance Profiling:
        connect() -> get_processes(windowFilter) only if connect() reports multiple candidates -> get_visual_count -> get_render_stats -> find_binding_leaks(threshold=50) -> measure_element_render_time(elementId)

        More workflows live in MCP resources and prompts, starting with wpf://workflows/starter-path.

        === NORMALIZATION & CONTRACT NOTES ===
        Some tools normalize inputs or compact outputs. Read wpf://contracts/response for normalized fields, compatibility aliases, detail=compact/minimal/verbose, standard shape notes, trace truncation metadata, Binding-backed rollback markers, cleanupIncomplete/cleanupFailed semantics, explicit opt-out behavior, and get_binding_errors compact=false behavior.
        The MCP JSON-RPC envelope is parsed by the MCP C# SDK transport. This server validates tool-call names and arguments at the call-tool filter/tool boundary, and validates Inspector IPC request ids, methods, and correlation ids before dispatch into the injected host.

        === RESPONSE CONTRACT VERSION ===
        - Current response contract version: {{ResponseContractVersion.Current}}
        - Machine-readable response contract resource: `wpf://contracts/response` (descriptive JSON contract for `structuredContent`, `navigation`, `nextSteps`, `contextRefs`, canonical `recovery` error guidance, `parameterVocabularies`, compatibility aliases, detail=compact, detail=minimal, detail=verbose, standard response notes, and the `get_binding_errors` `navigation=false` explicit opt-out)
        - Machine-readable canonical tool manifest resource: `wpf://contracts/tools` (generated from source `[McpServerTool]` registration metadata, method signatures, capability tags, and policy annotations)
        - By default, tool responses include the additive `navigation` envelope; prefer `navigation.recommended` as the preferred follow-up surface when present instead of ad hoc tool guessing.
        - By default, tool responses also include compatibility `nextSteps`; expect `nextSteps: []` when no deterministic guidance exists.
        - session-aware `nextSteps` may include `preconditions`, `expectedOutcome`, `workflowId`, `prefetchTools`, `whyNow`, and confidence; all are advisory.
        - `navigation` includes recommended/alternatives/prefetchTools plus descriptive `contextRefs`; `nextSteps` remains a compatibility field.
        - If you already know the next step, get_binding_errors accepts navigation=false. Schema-driven clients can rely on that opt-out there because the parameter is advertised in the tool schema today.
        - Compatibility aliases include currentValue -> effectiveValue, typeName -> viewModelType, avgRenderTime -> averageFrameTime, count -> totalCount, and renderTimeMs -> renderTime.

        === ERROR RECOVERY ===
        - "not connected" -> call connect(processId) first, then retry
        - "Access denied" / elevated targets -> restart the MCP server as administrator
        - "Not a WPF application" -> use get_processes to find correct processId
        - "Architecture mismatch" -> Architecture matching is mandatory for raw injection/bootstrapper fallback. Use a package/build whose server, bootstrapper, and Inspector sidecar all match the target bitness. SDK-hosted reuse communicates over named pipes and does not require matching process bitness once the target-side host is already running
        - "signature verification failed" (errorCode: SecurityError) -> use a Debug build for local development (auto-skips verification for local DLLs), or sign the Inspector DLL with Authenticode for production
        - "timeout" -> process may be frozen; try ping() to verify connection
        - existing SDK host security mismatch (errorCode: SecurityError) -> verify WPFDEVTOOLS_AUTH_SECRET matches and WPFDEVTOOLS_CERT_DIR is the same local absolute path in both processes. Network paths are not allowed. Hardened SDK mode requires setting both values together before calling InspectorSdk.Initialize(), and the default-hardened MCP server will not reuse a plaintext SDK host.
        - connect() returns SecurityError with requiresExplicitTargetOptIn=true -> raw injection requires an exact executable allowlist entry. Prefer InspectorSdk.Initialize() for target-side reuse, or explicitly allowlist the exact local absolute executable path in WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS before retrying.
        - InvalidPolicyConfiguration with WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS or WPFDEVTOOLS_MCP_ALLOWED_TARGETS -> fix malformed entries to exact local absolute executable paths and restart.
        - SecurityError with policyEnvVar=WPFDEVTOOLS_MCP_ALLOWED_TARGETS -> add the reviewed exact local absolute executable path before retrying.
        - SecurityError from a WPFDEVTOOLS_MCP_ALLOW_* gate -> use an allowed inspection workflow or ask the operator to enable that capability; InvalidPolicyConfiguration means fix the malformed boolean value.
        - existing SDK host build/protocol mismatch (errorCode: CompatibilityError) -> restart the target process so connect() can inject or reuse an Inspector host built from the same repo revision and compatibility contract as the MCP server
        - SDK startup fails closed before host reuse is possible -> set both WPFDEVTOOLS_AUTH_SECRET and WPFDEVTOOLS_CERT_DIR before calling InspectorSdk.Initialize(); partial or unset SDK transport configuration is no longer accepted by default
        - "element not found" -> verify elementId from get_visual_tree/get_logical_tree
        - "property not found" -> verify propertyName spelling and element type
        - "Rate limit exceeded" -> wait the returned `retryAfterSeconds`, then retry. Response includes { availableTokens, retryAfterSeconds, retryAfter }
        - InternalError/FileNotFound/OperationError -> retry when safe, verify build output, or report the issue with errorData

        === RESPONSE CONTRACT ===
        All tool responses include a JSON `success` field plus tool-specific structuredContent fields. Exact field contracts live in `wpf://contracts/response`; selected high-value tools also advertise closed `tools/list` outputSchema.
        Error responses set `success=false` and may include `error`, `errorCode`, `errorData`, `recovery`, `hint`, `suggestedAction`, `requiresReconnect`, or `stateAfterTimeoutUnknown` depending on the failure.
        - errorCode is the Inspector error enum name when the request reached the in-process Inspector
        - errorData is optional structured context for automated recovery logic
        - recovery is the canonical machine-readable recovery surface; compatibility fields such as suggestedAction, requiresReconnect, stateAfterTimeoutUnknown, retryAfterSeconds, retryAfter, availableTokens, and availableEvents may remain top-level for older clients
        - Common closed vocabularies for string parameters such as windowFilter, selectionStrategy, depthMode, detail, and outputMode are published in parameterVocabularies inside `wpf://contracts/response`

        === LIMITATIONS ===
        - STDIO transport: Cannot push live watcher/event streams; use request-response diagnostics and polling workflows
        - Self-contained single-file WPF apps: raw injection is unavailable, but the single-file WPF workflow remains available through the target-side SDK host; start InspectorSdk.Initialize() with matching transport settings so connect() can reuse the existing pipe-backed InspectorHost
        - Native AOT apps: not supported; SDK-hosted reuse is not a Native AOT workaround because the Inspector SDK requires managed WPF runtime and assembly access
        - some trimmed apps: publish trimming may remove required types, making raw injection or SDK-host startup unreliable; prefer SDK-host reuse as the fallback, but do not assume it restores full compatibility
        - elevated targets: a non-administrator MCP server cannot inject into or control an administrator-launched WPF process
        - Changes are NOT persisted to XAML files
        """;
}
