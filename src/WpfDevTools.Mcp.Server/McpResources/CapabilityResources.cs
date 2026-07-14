using System.ComponentModel;
using ModelContextProtocol.Server;
using WpfDevTools.Mcp.Server.Schema;

namespace WpfDevTools.Mcp.Server.McpResources;

[McpServerResourceType]
public static partial class CapabilityResources
{
    private const string ResponseContractResourceUri = "wpf://contracts/response";


    [McpServerResource(
        Name = "wpf_capabilities",
        Title = "WPF Capabilities",
        UriTemplate = "wpf://capabilities",
        MimeType = "text/markdown")]
    [Description("Capability summary for discovery, transport limits, server metadata, and high-value workflows.")]
    public static string GetCapabilities() =>
        $"""
        # WPF DevTools MCP Capabilities

        - Server: `wpf-devtools-mcp`
        - Version: `{ServerMetadata.GetDisplayVersion()}`
        - Schema version: `{ServerMetadata.GetSchemaVersion()}`
        - Response contract version: `{ResponseContractVersion.Current}`
        - Transport: `stdio`
        - Tool surface: WPF process discovery, connection, exact-match element search, tree inspection, binding diagnostics, DependencyProperty analysis, MVVM inspection, style/template inspection, interaction, layout, performance, and routed-event diagnostics
        - Prompt surface: workflow entry points for start diagnostics, connection, binding diagnosis, command/click diagnosis, elevated-target diagnosis, performance profiling, and secondary-window inspection
        - Resource surface: capability summary, response contract JSON, canonical tool manifest JSON, structured tool input examples, compact contract reconstruction index, bounded contract chunks, workflow references, retained screenshot PNG resources, elevated-target limitations, injection failure notes, window/focus limitations, performance profiling notes, and runtime state safety notes
        - Feature flags: `prompts=true`, `resources=true`, `stateSnapshots=true`, `diagnosticNormalization=true`, `elevatedTargetDiagnostics=true`

        ## Recommended workflow shape

        - Confirm `WPFDEVTOOLS_MCP_ALLOWED_TARGETS` contains the reviewed target's exact local absolute executable path; unset or malformed values fail closed before `connect()` attaches.
        - Start with `connect()` and let auto-discovery pick the single visible allowlisted WPF target when possible.
        - Enable `WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS=true` only for sessions where target UI text, DependencyProperty or binding values, event payloads, tree/scene summaries, or state snapshots may leave the target process.
        - Call `get_processes(windowFilter)` only when `connect()` reports multiple candidates or when you explicitly need a filtered process list before connecting.
        - Prefer `get_ui_summary`, `get_element_snapshot`, or `get_form_summary` before tree-heavy inspection.
        - After each diagnostic, interaction, or mutation, prefer returned `navigation.recommended`; treat compatibility `nextSteps` as the fallback for older clients before guessing the next tool.

        ## Response contract notes

        - Machine-readable JSON contract resources: `wpf://contracts/response` for stable response fields, `wpf://contracts/tools` for the canonical tool manifest, and `wpf://contracts/tool-examples` for complex input examples.
        - If a client bridge truncates one of those JSON resources, read `wpf://contracts/index`, fetch its advertised UTF-8 chunks sequentially at no more than 16 KiB, concatenate the decoded bytes, then verify the published byte length and SHA-256 before parsing.
        - Read `wpf://contracts/response` when clients need stable field-level metadata for `structuredContent`, `navigation`, `nextSteps`, `contextRefs`, canonical `recovery` error guidance, closed vocabularies for common enum-like parameters, and the `get_binding_errors` `navigation=false` opt-out without relying on prose alone.
        - By default, every tool response includes compatibility `nextSteps`; tools without runtime-computable guidance return `nextSteps: []`.
        - By default, responses also include a `navigation` envelope with `recommended`, `alternatives`, `prefetchTools`, and `contextRefs`.
        - `nextSteps` remains a compatibility field and is derived from `navigation.recommended` unless `get_binding_errors` explicitly receives `navigation=false`.
        - Clients may request `navigation=false` on `get_binding_errors` as an explicit opt-out to omit both `navigation` and compatibility `nextSteps` from a response. Schema-driven clients can rely on that opt-out there because the parameter is advertised in the tool schema today; do not assume other tool schemas expose that parameter unless they advertise it explicitly.
        - v2 adds optional `preconditions`, `expectedOutcome`, `workflowId`, `prefetchTools`, `whyNow`, and `confidence` fields on `nextSteps` entries.
        - `contextRefs` are descriptive JSON only; they are not executable handles or hidden server-side orchestration tokens.
        - `prefetchTools` is advisory only and contains tool names for clients that can load nearby schemas progressively.
        - Compatibility aliases remain in the current response contract to avoid breaking existing clients.
        - Compatibility aliases:
          - `currentValue -> effectiveValue`
          - `typeName -> viewModelType`
          - `avgRenderTime -> averageFrameTime`
          - `count -> totalCount`
          - `renderTimeMs -> renderTime`
        - `get_binding_errors` defaults to compact output; pass `compact=false` when the full free-form binding trace message is required.
        - Mutation and interaction tools default to `detail=compact` when clients want to drop additive normalization metadata without changing core semantics.
        - Use `detail=minimal` when mutation flows only need success/property/newValue confirmation.
        - Use `detail=verbose` when clients need requested/effective input plus observedEffect metadata; legacy `detail=standard` remains accepted as a compatibility alias.
        - `drain_events` and piggybacked `pendingEvents` payloads may also surface `cleanupIncomplete`, `cleanupFailureMessage`, and `cleanupFailureType` when buffered-event cleanup could not finish cleanly.
        - When replay is already buffered, `drain_events` performs an uncapped live read internally, then applies the caller-visible `maxEvents`, `eventTypes`, `elementId`, and `sinceTimestamp` filters across the merged replay + live event set.
        - In that replay-present path, any replay event that is not returned by the explicit read, and any matching live event that exceeds the caller-visible result cap, remain buffered for the next explicit `drain_events` read.
        - If a replay-backed live drain fails before merge completes, the error surfaces `errorData.replayPreserved=true` plus `errorData.bufferedReplayEventCount` so clients can retry without assuming the preserved replay buffer was discarded.

        ## Transport notes

        - STDIO mode is request/response oriented.
        - Watchers and event traces should be treated as polling-oriented workflows, not true push subscriptions.
        - Treat prompt names and resource URIs as the portable discovery contract across clients.
        - Prompt discovery can surface as slash commands in clients that support MCP prompts.
        - Resource discovery can surface through `@resource` references in clients that support MCP resources.

        ## Known capability boundaries

        - Elevated targets require the MCP server itself to run as administrator.
        - Main-window targeting is the default when `elementId` is omitted.
        - Runtime mutations are not persisted to XAML.
        - Snapshot restore currently supports DependencyProperty local values, Binding-backed DependencyProperties captured in the same session, scalar ViewModel values, and focus restoration.
        """;


    [McpServerResource(
        Name = "wpf_starter_path",
        Title = "Starter Tool Path",
        UriTemplate = "wpf://workflows/starter-path",
        MimeType = "text/markdown")]
    [Description("Compact starter workflow for progressive-discovery clients that keep only the most common WPF tools loaded initially.")]
    public static string GetStarterPathWorkflow() =>
        """
        # Starter Tool Path

        Use this compact path when the client is using progressive tool discovery and should keep only the highest-value WPF tools in the initial context.

        1. Confirm `WPFDEVTOOLS_MCP_ALLOWED_TARGETS` contains the reviewed exact local absolute executable path and `WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS=true` is set for scene, binding, DP, and state reads.
        2. Call `connect()`.
        3. Call `get_ui_summary(depthMode="semantic", summaryOnly=true)` for first scene context.
        4. Follow returned `navigation.recommended` before guessing the next tool.
        5. When you have a concrete `elementId`, call `get_element_snapshot(elementId)`.
        6. Use `get_form_summary` for form readiness or `get_bindings(elementId)` for binding-specific follow-up.
        7. Search or load specialized tools only after the scene or binding result narrows the task.

        Keep `connect`, `get_ui_summary`, `get_element_snapshot`, `get_bindings`, and `get_form_summary` as the primary always-visible tools. Use `wpf://contracts/tools` for the canonical manifest, categories, capability tags, policy gates, parameter metadata, and reflection-backed constraints before loading the rest of the full tool catalog.
        """;


    [McpServerResource(
        Name = "wpf_binding_workflow",
        Title = "Binding Workflow",
        UriTemplate = "wpf://workflows/binding-debug",
        MimeType = "text/markdown")]
    [Description("Canonical workflow for binding errors, value-chain tracing, DataContext inheritance, and validation correlation.")]
    public static string GetBindingWorkflow() =>
        """
        # Binding Debug Workflow

        Use this when UI data is blank, wrong, or stale.

        1. Confirm `WPFDEVTOOLS_MCP_ALLOWED_TARGETS` contains the reviewed target's exact local absolute executable path; unset or malformed values fail closed before `connect()` attaches.
        2. `connect()`
        3. If `connect()` reports multiple candidates, call `get_processes(windowFilter)` and retry `connect(processId)`
        4. `get_binding_errors`
        5. Follow `navigation.recommended` or `nextSteps` from the latest diagnostic result
        6. If the latest diagnostic still leaves the failing element ambiguous, call `get_element_snapshot` for one-call local context
        7. `get_bindings`
        8. `get_binding_value_chain`
        9. `get_datacontext_chain`
        10. `get_validation_errors` when validation may block updates

        Cross-tool semantics:
        - `get_binding_errors` reports failures captured by WPF binding tracing and returns compact payloads by default.
        - `get_binding_value_chain` explains how one binding resolved.
        - `get_datacontext_chain` explains inherited source context.
        - `get_validation_errors` explains rule-based invalid state on the element subtree.
        """;

    [McpServerResource(
        Name = "wpf_elevated_target_limitations",
        Title = "Elevated Target Limitations",
        UriTemplate = "wpf://limitations/elevated-targets",
        MimeType = "text/markdown")]
    [Description("Explains why allowlisted elevated targets may be discoverable but still reject connect or control operations.")]
    public static string GetElevatedTargetLimitations() =>
        """
        # Elevated Target Limitations

        A non-administrator MCP server can often discover an allowlisted elevated WPF process, but it cannot inject into or control it.

        Expected behavior:
        - `get_processes` may list an allowlisted target and mark `isElevated` / `requiresElevationToConnect`.
        - `connect` may fail with `AccessDenied`.
        - Interaction and mutation tools also require matching privilege if the target is elevated.

        In stdio mode, the MCP server inherits the privilege level of the host client process.
        To control an administrator-launched target, start the host client and MCP server as administrator.
        """;

    [McpServerResource(
        Name = "wpf_injection_failure_limitations",
        Title = "Injection Failure Limitations",
        UriTemplate = "wpf://limitations/injection-failures",
        MimeType = "text/markdown")]
    [Description("Summarizes high-signal injection failure causes, packaging constraints, and the current SDK-mode escape hatch.")]
    public static string GetInjectionFailureLimitations() =>
        """
        # Injection Failure Limitations

        Common high-signal failure classes:

        - `architecture mismatch`: Architecture matching is mandatory for raw injection/bootstrapper fallback. SDK-hosted reuse communicates over named pipes and does not require matching process bitness once the target-side host is already running.
        - `access denied`: target privilege is higher than the MCP host process.
        - `timeout`: target process is hung, blocked, or the inspector pipe never became available.
        - `injection-constrained packaging`: single-file targets cannot use raw injection. Native AOT is unsupported; SDK-hosted reuse is not a Native AOT workaround. Trimmed targets remain risky because publish trimming may remove required types needed by either raw injection or SDK-host startup.
        - `security software interference`: AV / endpoint controls may block DLL injection or named pipes.

        Current mitigation guidance:

        - Prefer matching architecture builds.
        - Run the MCP host as administrator for elevated targets.
        - Use SDK mode by starting the target-side host with InspectorSdk.Initialize() and matching transport settings, including the same local absolute WPFDEVTOOLS_CERT_DIR value when TLS is enabled. Network paths are not allowed. Set WPFDEVTOOLS_AUTH_SECRET and WPFDEVTOOLS_CERT_DIR together on both sides before initialization; if either value is missing, SDK startup now fails closed.
        - SDK mode keeps the single-file WPF workflow available. Native AOT remains unsupported. For trimmed apps it remains the preferred fallback, not a guarantee, and the default-hardened MCP server will not reuse a plaintext SDK host.
        """;


    [McpServerResource(
        Name = "wpf_window_focus_limitations",
        Title = "Window And Focus Limitations",
        UriTemplate = "wpf://limitations/window-focus",
        MimeType = "text/markdown")]
    [Description("Clarifies main-window defaults, multi-window targeting rules, and focus-sensitive behavior.")]
    public static string GetWindowFocusLimitations() =>
        """
        # Window And Focus Limitations

        Multi-window WPF inspection is supported, but targeting rules matter:

        - Omitting `elementId` targets `Application.MainWindow`, not the currently focused window.
        - Use `get_windows` first when dialogs, popups, or tool windows are present.
        - Re-check `get_windows` after focus or visibility changes.
        - Prefer explicit window `elementId` targeting for tree inspection, screenshots, and interactions.
        - `focus_element` and `simulate_keyboard` require the target to be attached to the active rendered visual tree; controls inside inactive tabs must be activated before focus-sensitive actions.

        This avoids accidental inspection of the wrong root when secondary windows are active.
        """;

    [McpServerResource(
        Name = "wpf_performance_profiling_notes",
        Title = "Performance Profiling Notes",
        UriTemplate = "wpf://limitations/performance-profiling",
        MimeType = "text/markdown")]
    [Description("Explains warm-up requirements, polling cadence, and interpretation guidance for performance tools.")]
    public static string GetPerformanceProfiling() =>
        """
        # Performance Profiling Notes

        Performance tools require specific awareness for correct interpretation:

        ## Warm-up
        - `get_render_stats` returns zeros on the first call because the Inspector has not yet observed a render cycle.
        - Call `get_render_stats` once to start monitoring, wait a few seconds, then call again for meaningful data.

        ## Polling cadence
        - Avoid calling `get_render_stats` or `measure_element_render_time` in tight loops.
        - Use one-shot polling: call, wait, call again. The rate limit (300 req/min) applies.

        ## Interpretation
        - `get_visual_count` > 5000 elements may indicate a complex tree worth optimizing.
        - `find_binding_leaks(threshold)` reports binding references above the threshold; high counts may indicate leaked DataContext references.
        - `measure_element_render_time` isolates rendering cost to a single element subtree.

        ## Session scope
        - Performance metrics are session-scoped and reset on reconnect.
        - Monitoring starts when the Inspector DLL is injected and stops on process disconnect.
        """;

    [McpServerResource(
        Name = "wpf_state_safety_notes",
        Title = "Runtime State Safety Notes",
        UriTemplate = "wpf://limitations/state-safety",
        MimeType = "text/markdown")]
    [Description("Summarizes the shipped mutation safety model, snapshot boundaries, and rollback expectations.")]
    public static string GetStateSafetyNotes() =>
        """
        # Runtime State Safety Notes

        Mutation tools change the live application state but do not persist to XAML.

        Current safety model:
        - Changes are process-local and reset on app restart.
        - Snapshot/restore is session-scoped, not durable persistence.
        - `capture_state_snapshot` can capture DependencyProperty local values, scalar ViewModel values, and focus state.
        - Binding-backed DependencyProperties captured in the same session can be restored through the saved restore handle; non-Binding expressions are still surfaced as skipped capability boundaries instead of pretending they can be reconstructed.
        - `restore_state_snapshot` can replay captured local-value DependencyProperties, scalar ViewModel values, and focus state in the same connected session.
        - Prefer minimal mutations and capture a snapshot before a debugging sequence when rollback matters.

        Use this guidance in demos, troubleshooting, and regression flows to avoid cross-test contamination.
        """;
}
