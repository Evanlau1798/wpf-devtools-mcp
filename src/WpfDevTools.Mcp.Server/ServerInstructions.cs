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
        1. Confirm WPFDEVTOOLS_MCP_ALLOWED_TARGETS contains the reviewed target's exact absolute executable path; unset or malformed values fail closed with SecurityError before connect() attaches
        2. connect() -> try auto-discovery against visible allowlisted WPF apps, then reuse a compatible existing SDK host or apply the raw-injection target policy before injecting Inspector DLL
        3. If connect() reports multiple candidates, call get_processes(windowFilter) and retry connect(processId)
        4. Build initial context with get_ui_summary or get_form_summary before expanding trees; use get_element_snapshot(elementId) only after a concrete elementId is known
        5. Use focused inspection/interaction tools against the connected process
        6. Do not call get_processes before connect() unless auto-discovery is ambiguous or you explicitly need filtered discovery before connecting

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
        - For large toolsets, defer loading specialized tools until the task scope is known; start with scene/process/binding summaries, then search for narrower tools
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
        - Prefer scene tools first: get_ui_summary defaults to depth=3 and depthMode='semantic', so layout-only wrappers do not consume the initial scene budget
        - Tree tools default to depth=10 when depth is omitted, but initial tree exploration should usually pass a smaller explicit depth
        - Use depth=1 for immediate children only (fastest)
        - Use depth=2-3 for typical UI exploration (recommended)
        - Use depth=5+ only when deep tree analysis is necessary
        - Use elementId to scope tools to a subtree instead of full tree
        - Use nameFilter on get_processes to reduce response size

        === AI AGENT BEST PRACTICES ===
        - Confirm WPFDEVTOOLS_MCP_ALLOWED_TARGETS contains the reviewed target's exact absolute executable path before connect(); unset or malformed values fail closed
        - Start with connect() after the target is allowlisted unless you already know you need a specific processId or non-default windowFilter
        - After connect() succeeds, immediately build context with get_ui_summary or get_form_summary before tree-heavy inspection or screenshots; use get_element_snapshot(elementId) only after a concrete elementId is known
        - When connect() reports multiple candidates, use get_processes(windowFilter) to disambiguate and retry
        - If connect() returns SecurityError with requiresExplicitTargetOptIn=true, prefer SDK-hosted reuse first. Only use WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS for explicitly reviewed external executables.
        - Do not call get_processes before connect() as a default habit; treat it as a disambiguation or filtering tool
        - When hidden or background targets matter, prefer connect(windowFilter='all') instead of listing processes first just to widen auto-discovery
        - When multiple WPF processes are expected and largest-target auto-selection is intentional, prefer connect(selectionStrategy='largest_working_set', windowFilter='all') instead of a separate get_processes round trip
        - Store processId in conversation context after successful connect()
        - Prefer get_ui_summary/get_form_summary before tree-heavy inspection; use get_element_snapshot(elementId) only after a concrete elementId is known
        - Prefer get_ui_summary(summaryOnly=true, depthMode='semantic') for initial scene orientation unless you already have a narrow element-centric question
        - Use depth=2-3 for initial tree exploration; increase only if needed
        - Batch related operations in single turn (e.g., get_visual_tree + get_bindings)
        - For clients that can issue concurrent calls, run independent read-only inspections in parallel when the client supports it, then summarize intermediate tool results before choosing the next call
        - For high-cardinality responses, keep only decision-relevant fields in conversation state and rely on elementId/snapshotId/resource URI references for follow-up calls
        - For tools with complex parameters, remember that complex parameters require concrete examples; prefer copied examples from the tool description over invented shapes
        - Prefer prompt names and resource URIs as the portable discovery contract when you want a predefined workflow entry point or capability summary
        - Some clients surface those prompt/resource contracts as slash commands or @resource lookups; use the client-specific rendering only as a convenience layer
        - Check IsEnabled with get_dp_value_source before click_element to avoid errors
        - Use get_binding_errors as first diagnostic step for data display issues; its default response is compact, so pass compact=false only when the full human-readable binding trace text is required
        - Treat returned runtime navigation as session-aware when you have already captured a snapshot or started a routed-event trace in the same connected process
        - When you already know the next step and do not need server guidance, get_binding_errors accepts navigation=false to omit navigation and compatibility nextSteps on that specific call; schema-driven clients may rely on that opt-out there because the parameter is advertised in the tool schema today. Do not generalize that opt-out to other tools unless their schema explicitly advertises it too.
        - In STDIO transport, prefer polling workflows over push-style watcher/event streaming expectations
        - wait_for_dp_change is now read-only; if you need the old triggerMutation-style serialized mutation-plus-wait flow, use wait_for_dp_change_after_mutation instead
        - Avoid calling performance tools (get_render_stats, measure_element_render_time) in loops
        - When debugging, start broad (get_binding_errors) then narrow (get_bindings on specific element)
        - For MVVM apps, inspect ViewModel first (get_viewmodel, get_commands) before modifying
        - If get_dp_value_source reports isExpression=true, treat set_dp_value as a temporary override. Binding-backed expressions can usually be restored later in the same session with clear_dp_value or restore_state_snapshot; for two-way bindings, also snapshot the relevant ViewModel property when you need deterministic semantic rollback of the source value. Non-Binding expressions remain an explicit capability boundary
        - focus_element and simulate_keyboard require the target to be attached to the active rendered visual tree; activate inactive tabs before focus-sensitive actions
        - Remember: all destructive changes are runtime-only and NOT persisted to XAML

        === SERVER-SIDE POLICY GATES ===
        - Operators must configure WPFDEVTOOLS_MCP_ALLOWED_TARGETS with a semicolon-separated exact absolute executable path allowlist before connect(); unset or malformed configured entries fail closed
        - WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS=true opts into runtime mutation, interaction, render-measurement, and session state-consuming tools such as capture_state_snapshot and drain_events before they reach the target process
        - WPFDEVTOOLS_MCP_ALLOW_SCREENSHOTS=true opts into element_screenshot at the MCP boundary
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

        All common workflows assume WPFDEVTOOLS_MCP_ALLOWED_TARGETS already contains the reviewed target's exact absolute executable path; unset or malformed values fail closed before connect() attaches.

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
        connect() -> get_processes(windowFilter) only if connect() reports multiple candidates -> get_event_handlers(elementId, eventName) -> trace_routed_events(mode="start", eventName) -> click_element(elementId) -> drain_events(eventTypes=['RoutedEvent'], elementId)

        Workflow 7 - Debug Validation Errors:
        connect() -> get_form_summary -> get_validation_errors() [root-scope aggregates all descendants] -> get_validation_errors(elementId) [narrow to specific element] -> get_bindings(elementId)

        Workflow 8 - Safe Mutation Session:
        connect() -> capture_state_snapshot(processId, elementId, propertyNames/viewModelPropertyNames, includeFocus=true) -> perform runtime mutations -> get_state_diff(snapshotId, trigger='...') -> restore_state_snapshot(processId, snapshotId)

        === NORMALIZATION & CONTRACT NOTES ===
        Some tools apply automatic normalization. Responses include metadata so you can see what was normalized:
        - trace_routed_events (start mode): enforces minimum 30s duration for AI agent IPC round-trips. Response includes both requestedDuration and effectiveDuration.
        - trace_routed_events (get/capture): maxEvents caps returned trace records. Check returnedEventCount, totalEventCount, eventsTruncated, and maxEvents before assuming the events array is complete.
        - trace_routed_events cleanup: cleanupState can report deferredPending, deferredCompleted, deferredFailed, or failed. Treat cleanupFailed/cleanupIncomplete as state signals, not as proof that handlers remain attached.
        - fire_routed_event: for ButtonBase + Click event, uses OnClick() path instead of RaiseEvent(). Response includes usedOnClick: true.
        - get_validation_errors: recursively aggregates errors from ALL visual descendants (max depth: 50, max errors: 200). Each error includes elementType/elementName to identify the source.
        - set_dp_value / modify_viewmodel: JSON string values with surrounding double-quotes are auto-stripped (e.g., "\"hello\"" becomes "hello").
        - set_dp_value reports replacedExpression=true when it overwrote an expression-backed DependencyProperty with a local value; capturedRollbackExpression=true means the previous Binding/MultiBinding/PriorityBinding was captured for same-session rollback.
        - clear_dp_value may report restoredExpression=true when it reapplies a captured Binding-backed expression instead of merely clearing to default/inherited state.
        - restore_state_snapshot can restore Binding-backed DependencyProperty expressions captured in the same session; non-Binding expressions are still reported as skipped capability boundaries.
        - binding/data-context/validation diagnostics expose normalized diagnosticKind/sourceKind fields for cross-tool correlation.
        - get_binding_errors defaults to compact output at the MCP contract layer while preserving server-side follow-up guidance; use compact=false when you need the original verbose message text.
        - mutation and interaction tools default to detail=compact when you want trimmed additive normalization metadata while preserving the same core semantics.
        - use detail=minimal when a mutation workflow only needs success/property/newValue confirmation.
        - use detail=verbose when you need requested/effective input plus observedEffect metadata; legacy detail=standard remains accepted as a compatibility alias.

        === RESPONSE CONTRACT VERSION ===
        - Current response contract version: {{ResponseContractVersion.Current}}
        - Machine-readable response contract resource: `wpf://contracts/response` (JSON contract for `structuredContent`, `navigation`, `nextSteps`, `contextRefs`, canonical `recovery` error guidance, `parameterVocabularies`, compatibility aliases, and the `get_binding_errors` `navigation=false` opt-out)
        - By default, tool responses include the additive `navigation` envelope; prefer `navigation.recommended` as the preferred follow-up surface when present instead of ad hoc tool guessing.
        - By default, tool responses also include compatibility `nextSteps`; expect `nextSteps: []` when the server has no deterministic runtime guidance.
        - v2 `nextSteps` entries may also include optional `preconditions`, `expectedOutcome`, `workflowId`, `prefetchTools`, `whyNow`, and `confidence` fields.
        - v3 `navigation` includes `recommended`, `alternatives`, `prefetchTools`, and descriptive `contextRefs` entries.
        - `nextSteps` remains a compatibility field and is derived from `navigation.recommended` for clients that ignore the richer envelope.
        - Clients may use `navigation=false` as an explicit opt-out on `get_binding_errors` to omit both `navigation` and compatibility `nextSteps` on that specific call. Schema-driven clients can rely on that opt-out there because the parameter is advertised in the tool schema today; do not assume other tool schemas expose that parameter unless they advertise it explicitly.
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
        - "Architecture mismatch" -> MCP server and target process must have matching architectures (both x64 or both x86). Use a package/build whose server, bootstrapper, and Inspector sidecar all match the target bitness
        - "signature verification failed" (errorCode: SecurityError) -> use a Debug build for local development (auto-skips verification for local DLLs), or sign the Inspector DLL with Authenticode for production
        - "timeout" -> process may be frozen; try ping() to verify connection
        - existing SDK host security mismatch that completes an incompatible authenticated/TLS handshake (errorCode: SecurityError) -> verify WPFDEVTOOLS_AUTH_SECRET matches and WPFDEVTOOLS_CERT_DIR is the same local absolute path in both the MCP server and target app. Network paths are not allowed. For connect() reuse, hardened SDK mode requires setting both values together before calling InspectorSdk.Initialize()
        - connect() returns SecurityError with requiresExplicitTargetOptIn=true -> raw injection requires an exact executable allowlist entry. Prefer InspectorSdk.Initialize() for target-side reuse, or explicitly allowlist the exact absolute executable path in WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS before retrying.
        - connect() returns SecurityError with policyEnvVar=WPFDEVTOOLS_MCP_ALLOWED_TARGETS -> the target executable is outside the configured MCP target allowlist. Retry only after the exact absolute executable path is reviewed and added.
        - tool call returns SecurityError from a WPFDEVTOOLS_MCP_ALLOW_* gate -> the server policy disabled that capability for this session. Use an allowed inspection workflow or ask the operator to explicitly enable the gate.
        - tool call returns InvalidPolicyConfiguration -> fix the malformed WPFDEVTOOLS_MCP_ALLOW_* value to true or false and restart the MCP server.
        - existing SDK host build/protocol mismatch (errorCode: CompatibilityError) -> restart the target process so connect() can inject or reuse an Inspector host built from the same repo revision and compatibility contract as the MCP server
        - SDK startup fails closed before host reuse is possible -> set both WPFDEVTOOLS_AUTH_SECRET and WPFDEVTOOLS_CERT_DIR before calling InspectorSdk.Initialize(); partial or unset SDK transport configuration is no longer accepted by default
        - existing plaintext or otherwise unresponsive SDK host may still surface as Timeout -> restart the target host or enable explicit matching transport settings before retrying connect. The default-hardened MCP server will not reuse a plaintext SDK host
        - "element not found" -> verify elementId from get_visual_tree/get_logical_tree
        - "property not found" -> verify propertyName spelling and element type
        - "Rate limit exceeded" -> wait the returned `retryAfterSeconds`, then retry. Response includes { availableTokens, retryAfterSeconds, retryAfter }
        - errorCode "InternalError" -> an unexpected server error; retry or report the issue
        - errorCode "FileNotFound" -> required file is missing; verify build output
        - errorCode "OperationError" -> operation failed; check error message for details

        === RESPONSE FORMAT ===
        All tools return JSON: { success: boolean, ...fields }
        On error: { success: false, error: string, errorCode?: string, errorData?: object, recovery?: { hint?: string, suggestedAction?: string, requiresReconnect?: boolean, stateAfterTimeoutUnknown?: boolean, processId?: number, timeoutSeconds?: number, retryAfterSeconds?: number, retryAfter?: string, availableTokens?: number, availableEvents?: string[] }, hint?: string, suggestedAction?: string, requiresReconnect?: boolean, stateAfterTimeoutUnknown?: boolean, processId?: number, timeoutSeconds?: number, retryAfterSeconds?: number, retryAfter?: string, availableTokens?: number, availableEvents?: string[] }
        - errorCode is the Inspector error enum name when the request reached the in-process Inspector
        - errorData is optional structured context for automated recovery logic
        - recovery is the canonical machine-readable recovery surface; compatibility fields such as suggestedAction/requiresReconnect/stateAfterTimeoutUnknown/retryAfterSeconds remain projected at the top level for older clients
        - suggestedAction is a human-readable recovery hint when the next step is deterministic
        - requiresReconnect indicates the current pipe-backed session should be treated as stale before retrying
        - stateAfterTimeoutUnknown indicates a timeout may have left target state changed but unverified; reconnect and re-read before assuming success or failure
        - retryAfterSeconds and retryAfter are additive rate-limit backoff hints when throttling occurs
        - Common closed vocabularies for string parameters such as windowFilter, selectionStrategy, depthMode, detail, and outputMode are published in parameterVocabularies inside `wpf://contracts/response`

        === LIMITATIONS ===
        - STDIO transport: Cannot push live watcher/event streams; use request-response diagnostics and polling workflows
        - Self-contained single-file apps and Native AOT apps: raw injection is unavailable, but the overall WPF DevTools workflow remains available through the target-side SDK host; start InspectorSdk.Initialize() with matching transport settings so connect() can reuse the existing pipe-backed InspectorHost
        - some trimmed apps: publish trimming may remove required types, making raw injection or SDK-host startup unreliable; prefer SDK-host reuse as the fallback, but do not assume it restores full compatibility
        - elevated targets: a non-administrator MCP server cannot inject into or control an administrator-launched WPF process
        - Changes are NOT persisted to XAML files
        """;
}
